using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OrderService.Application.Common;
using OrderService.Domain.Common;
using OrderService.Domain.Orders;
using OrderService.Domain.Products;

namespace OrderService.Api.ErrorHandling;

/// <summary>
/// Mapeia exceções para <see cref="ProblemDetails"/> (RFC 7807): ValidationException
/// vira 400, OrderNotFoundException 404, InvalidOrderStateTransitionException 409,
/// ProductNotFoundException e demais DomainException 422, o resto 500 (logado,
/// com corpo genérico fora de Development). Tabela completa em decisions.md
/// seções 18 e 19.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IHostEnvironment environment, ILogger<GlobalExceptionHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        _environment = environment;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // OperationCanceledException (e TaskCanceledException, que herda
        // dela) é o cliente cancelando — conexão fechada ou timeout — não um
        // bug nosso. Tratamos antes do fallback de 500: loga como info, sem
        // sujar os logs de erro, e nem tenta escrever ProblemDetails numa
        // conexão que o cliente já abandonou.
        if (exception is OperationCanceledException)
        {
            _logger.LogInformation(
                "Request {Method} {Path} was canceled by the client.",
                httpContext.Request.Method,
                httpContext.Request.Path);

            return true;
        }

        var (statusCode, title) = MapException(exception);

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                exception,
                "Unhandled exception while processing {Method} {Path}.",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = $"https://orderservice.local/errors/{statusCode}",
            Detail = statusCode == StatusCodes.Status500InternalServerError && !_environment.IsDevelopment()
                ? "An unexpected error occurred. Please contact support if the problem persists."
                : exception.Message,
            Instance = httpContext.Request.Path,
        };

        // Correlaciona com o log estruturado — vale para qualquer status code, não só 500.
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        PopulateExtensions(problemDetails, exception);

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static (int StatusCode, string Title) MapException(Exception exception) => exception switch
    {
        ValidationException => (StatusCodes.Status400BadRequest, "Validation error"),
        OrderNotFoundException => (StatusCodes.Status404NotFound, "Order not found"),
        InvalidOrderStateTransitionException => (StatusCodes.Status409Conflict, "Invalid order state transition"),
        ProductNotFoundException => (StatusCodes.Status422UnprocessableEntity, "Product not found"),
        DomainException => (StatusCodes.Status422UnprocessableEntity, "Business rule violation"),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
    };

    /// <summary>Popula <see cref="ProblemDetails.Extensions"/> com os dados que cada exceção já carrega como propriedades tipadas — nunca fazendo parsing da mensagem.</summary>
    private static void PopulateExtensions(ProblemDetails problemDetails, Exception exception)
    {
        switch (exception)
        {
            case OrderNotFoundException e:
                problemDetails.Extensions["orderId"] = e.OrderId;
                break;
            case ProductNotFoundException e:
                problemDetails.Extensions["productId"] = e.ProductId;
                break;
            case DuplicateProductInOrderException e:
                problemDetails.Extensions["productId"] = e.ProductId;
                break;
            case InsufficientStockException e:
                problemDetails.Extensions["productId"] = e.ProductId;
                problemDetails.Extensions["requested"] = e.RequestedQuantity;
                problemDetails.Extensions["available"] = e.AvailableQuantity;
                break;
            case StockReleaseExceedsReservedException e:
                problemDetails.Extensions["productId"] = e.ProductId;
                problemDetails.Extensions["requested"] = e.RequestedQuantity;
                problemDetails.Extensions["reserved"] = e.ReservedQuantity;
                break;
            case InvalidOrderStateTransitionException e:
                problemDetails.Extensions["currentStatus"] = e.CurrentStatus.ToString();
                problemDetails.Extensions["attemptedStatus"] = e.AttemptedStatus.ToString();
                break;
            case CurrencyMismatchException e:
                problemDetails.Extensions["expectedCurrency"] = e.Expected.ToString();
                problemDetails.Extensions["actualCurrency"] = e.Actual.ToString();
                break;
            case InvalidQuantityException e:
                problemDetails.Extensions["quantity"] = e.Quantity;
                break;
            case InvalidPriceException e:
                problemDetails.Extensions["amount"] = e.Amount;
                break;
        }
    }
}
