using System.Globalization;
using OrderService.Api.Contracts;
using OrderService.Application.Common;
using OrderService.Application.Orders.CancelOrder;
using OrderService.Application.Orders.ConfirmOrder;
using OrderService.Application.Orders.CreateOrder;
using OrderService.Application.Orders.GetOrderById;
using OrderService.Application.Orders.ListOrders;
using OrderService.Domain.Orders;

namespace OrderService.Api.Endpoints;

/// <summary>Endpoints de negócio de <c>orders</c>. Todos exigem usuário autenticado, sem RBAC nem isolamento por customerId — é autorização básica, não dono do recurso (decisions.md seções 3 e 4).</summary>
public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/orders").WithTags("Orders").RequireAuthorization();

        group.MapPost("/", CreateOrderAsync);
        group.MapPost("/{id:guid}/confirm", ConfirmOrderAsync);
        group.MapPost("/{id:guid}/cancel", CancelOrderAsync);
        group.MapGet("/{id:guid}", GetOrderByIdAsync);
        group.MapGet("/", ListOrdersAsync);

        return app;
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        CreateOrderHandler handler,
        CancellationToken cancellationToken)
    {
        if (request.Items is null)
        {
            throw new ValidationException("items is required.");
        }

        var command = new CreateOrderCommand(
            request.CustomerId,
            request.Currency,
            request.Items.Select(i => new CreateOrderItemCommand(i.ProductId, i.Quantity)).ToArray());

        var order = await handler.HandleAsync(command, cancellationToken);

        return Results.Created($"/orders/{order.Id}", order);
    }

    private static async Task<IResult> ConfirmOrderAsync(
        Guid id,
        ConfirmOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var order = await handler.HandleAsync(new ConfirmOrderCommand(id), cancellationToken);
        return Results.Ok(order);
    }

    private static async Task<IResult> CancelOrderAsync(
        Guid id,
        CancelOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var order = await handler.HandleAsync(new CancelOrderCommand(id), cancellationToken);
        return Results.Ok(order);
    }

    private static async Task<IResult> GetOrderByIdAsync(
        Guid id,
        GetOrderByIdHandler handler,
        CancellationToken cancellationToken)
    {
        var order = await handler.HandleAsync(new GetOrderByIdQuery(id), cancellationToken);
        return Results.Ok(order);
    }

    private static async Task<IResult> ListOrdersAsync(
        Guid? customerId,
        string? status,
        string? from,
        string? to,
        int? page,
        int? pageSize,
        ListOrdersHandler handler,
        CancellationToken cancellationToken)
    {
        // status/from/to são parseados aqui na borda HTTP porque não é regra
        // de negócio da Application. page/pageSize seguem crus — a validação
        // de limites já mora em ListOrdersHandler (decisions.md seção 9).
        var parsedStatus = ParseStatusOrThrow(status);
        var fromUtc = ParseUtcDateOrThrow(from, "from");
        var toUtc = ParseUtcDateOrThrow(to, "to");

        var query = new ListOrdersQuery(customerId, parsedStatus, fromUtc, toUtc, page, pageSize);

        var result = await handler.HandleAsync(query, cancellationToken);

        return Results.Ok(result);
    }

    private static OrderStatus? ParseStatusOrThrow(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        if (!Enum.TryParse<OrderStatus>(status, ignoreCase: true, out var parsed))
        {
            throw new ValidationException(
                $"Invalid 'status' value '{status}'. Valid values: {string.Join(", ", Enum.GetNames<OrderStatus>())}.");
        }

        return parsed;
    }

    private static DateTime? ParseUtcDateOrThrow(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            throw new ValidationException($"Invalid '{parameterName}' date value '{value}'.");
        }

        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }
}
