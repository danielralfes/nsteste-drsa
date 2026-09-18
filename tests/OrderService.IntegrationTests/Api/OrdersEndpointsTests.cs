using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Api.Contracts;
using OrderService.Application.Common;
using OrderService.Application.Orders;
using OrderService.Domain.Common;
using OrderService.Domain.Orders;
using OrderService.Domain.Products;
using OrderService.Infrastructure.Persistence;

namespace OrderService.IntegrationTests.Api;

/// <summary>
/// Testes de integração HTTP fim a fim (WebApplicationFactory + Postgres via
/// Testcontainers). Cada teste que cria pedidos semeia seu próprio produto
/// com Id único, em vez de depender do catálogo fixo do <c>DatabaseSeeder</c>
/// — assim não sofre interferência dos outros testes que dividem o mesmo
/// container/banco dentro da collection "Api".
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OrdersEndpointsTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory _factory;
    private HttpClient _client = null!;

    public OrdersEndpointsTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // Endpoints de /orders/* exigem autenticação, então o client usado por
    // todos os testes deste fixture já sobe com o token pronto.
    public async Task InitializeAsync()
    {
        _client = await _factory.CreateAuthenticatedClientAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CreateOrder_ValidPayload_Returns201WithLocationAndCalculatedTotal()
    {
        var productId = await SeedProductAsync(unitPrice: 100.00m, availableQuantity: 10);

        var request = new CreateOrderRequest(
            Guid.NewGuid(),
            Currency.BRL,
            new[] { new CreateOrderItemRequest(productId, 3) });

        var response = await _client.PostAsJsonAsync("/orders", request, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var order = await response.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
        Assert.NotNull(order);
        Assert.Equal(300.00m, order!.Total);
        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Single(order.Items);
        Assert.Contains(order.Id.ToString(), response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task CreateOrder_WithoutItems_ReturnsProblemDetails()
    {
        var request = new CreateOrderRequest(Guid.NewGuid(), Currency.BRL, Array.Empty<CreateOrderItemRequest>());

        var response = await _client.PostAsJsonAsync("/orders", request, JsonOptions);

        // EmptyOrderException cai no DomainException genérico -> 422.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal(422, problem!.Status);
    }

    [Fact]
    public async Task CreateOrder_WithNonExistingProduct_Returns422ProductNotFound()
    {
        var unknownProductId = Guid.NewGuid();

        var request = new CreateOrderRequest(
            Guid.NewGuid(),
            Currency.BRL,
            new[] { new CreateOrderItemRequest(unknownProductId, 1) });

        var response = await _client.PostAsJsonAsync("/orders", request, JsonOptions);

        // ProductNotFoundException -> 422.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(unknownProductId.ToString(), body.GetProperty("productId").GetString());
    }

    [Fact]
    public async Task CreateOrder_WithDuplicateProductId_Returns422WithDuplicateProductId()
    {
        var productId = await SeedProductAsync(unitPrice: 10.00m, availableQuantity: 100);

        var request = new CreateOrderRequest(
            Guid.NewGuid(),
            Currency.BRL,
            new[]
            {
                new CreateOrderItemRequest(productId, 1),
                new CreateOrderItemRequest(productId, 2),
            });

        var response = await _client.PostAsJsonAsync("/orders", request, JsonOptions);

        // DuplicateProductInOrderException cai no DomainException genérico -> 422.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(422, body.GetProperty("status").GetInt32());
        Assert.Equal(productId.ToString(), body.GetProperty("productId").GetString());
    }

    [Fact]
    public async Task ConfirmOrder_WhenAlreadyCanceled_Returns409Conflict()
    {
        var orderId = await CreateOrderAsync(quantity: 1);

        var cancelResponse = await _client.PostAsync($"/orders/{orderId}/cancel", content: null);
        cancelResponse.EnsureSuccessStatusCode();

        var confirmResponse = await _client.PostAsync($"/orders/{orderId}/confirm", content: null);

        // InvalidOrderStateTransitionException -> 409, não 422 — é o caso
        // mais fácil de confundir com os DomainException genéricos.
        Assert.Equal(HttpStatusCode.Conflict, confirmResponse.StatusCode);

        var problem = await confirmResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(409, problem.GetProperty("status").GetInt32());
        Assert.Equal("Canceled", problem.GetProperty("currentStatus").GetString());
        Assert.Equal("Confirmed", problem.GetProperty("attemptedStatus").GetString());
    }

    [Fact]
    public async Task CreateOrder_LocationHeader_RoundTripsToSameOrderViaGet()
    {
        var productId = await SeedProductAsync(unitPrice: 12.50m, availableQuantity: 10);

        var request = new CreateOrderRequest(
            Guid.NewGuid(),
            Currency.BRL,
            new[] { new CreateOrderItemRequest(productId, 2) });

        var createResponse = await _client.PostAsJsonAsync("/orders", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
        var location = createResponse.Headers.Location;
        Assert.NotNull(location);

        var getResponse = await _client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var fetched = await getResponse.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
        Assert.NotNull(fetched);
        Assert.Equal(created!.Id, fetched!.Id);
        Assert.Equal(created.Total, fetched.Total);
    }

    [Fact]
    public async Task CreateOrder_Total_IsNumericWithTwoDecimalsAndMatchesSumOfItems()
    {
        var productA = await SeedProductAsync(unitPrice: 19.99m, availableQuantity: 10);
        var productB = await SeedProductAsync(unitPrice: 5.005m, availableQuantity: 10);

        var request = new CreateOrderRequest(
            Guid.NewGuid(),
            Currency.BRL,
            new[]
            {
                new CreateOrderItemRequest(productA, 3),
                new CreateOrderItemRequest(productB, 2),
            });

        var response = await _client.PostAsJsonAsync("/orders", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        var totalProperty = document.RootElement.GetProperty("total");

        // Total tem que vir como número JSON, não string.
        Assert.Equal(JsonValueKind.Number, totalProperty.ValueKind);

        var total = totalProperty.GetDecimal();

        // 19.99 * 3 = 59.97; 5.005 * 2 arredonda (MidpointRounding.AwayFromZero).
        // Checamos reconstruindo a partir dos itens retornados em vez de um
        // valor fixo hardcoded, pra não duplicar a regra de arredondamento aqui.
        var order = await response.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
        Assert.NotNull(order);
        var expectedTotal = order!.Items.Sum(i => i.LineTotal);
        Assert.Equal(expectedTotal, total);

        // Não pode ter mais de 2 casas decimais.
        Assert.True(decimal.Round(total, 2) == total, $"Total {total} has more than 2 decimal places.");
    }

    [Fact]
    public async Task ListOrders_WithAllFiltersCombined_ReturnsExpectedEnvelope()
    {
        var customerId = Guid.NewGuid();
        var productId = await SeedProductAsync(unitPrice: 8.00m, availableQuantity: 100);

        var otherCustomerId = Guid.NewGuid();
        // Ruído: pedido de outro cliente, não deve aparecer no resultado.
        await CreateOrderForCustomerAsync(otherCustomerId, productId, quantity: 1);

        var matchingOrderId = await CreateOrderForCustomerAsync(customerId, productId, quantity: 1);

        var from = DateTime.UtcNow.AddMinutes(-5).ToString("O", CultureInfo.InvariantCulture);
        var to = DateTime.UtcNow.AddMinutes(5).ToString("O", CultureInfo.InvariantCulture);

        var response = await _client.GetAsync(
            $"/orders?customerId={customerId}&status=Placed&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());

        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal(matchingOrderId.ToString(), items[0].GetProperty("id").GetString());
        Assert.Equal(customerId.ToString(), items[0].GetProperty("customerId").GetString());
        Assert.Equal("Placed", items[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Swagger_JsonDocument_IsAccessible()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("paths", out var paths));
        Assert.True(paths.TryGetProperty("/orders", out _));
    }

    [Fact]
    public async Task CreateOrder_ExceedingStock_ReturnsProblemDetailsWithProductRequestedAvailable()
    {
        var productId = await SeedProductAsync(unitPrice: 10.00m, availableQuantity: 5);

        var request = new CreateOrderRequest(
            Guid.NewGuid(),
            Currency.BRL,
            new[] { new CreateOrderItemRequest(productId, 1_000_000) });

        var response = await _client.PostAsJsonAsync("/orders", request, JsonOptions);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(productId.ToString(), body.GetProperty("productId").GetString());
        Assert.Equal(1_000_000, body.GetProperty("requested").GetInt32());
        Assert.Equal(5, body.GetProperty("available").GetInt32());
    }

    [Fact]
    public async Task ConfirmOrder_CalledTwice_IsIdempotentAndReturnsConfirmedBothTimes()
    {
        var orderId = await CreateOrderAsync(quantity: 2);

        var firstResponse = await _client.PostAsync($"/orders/{orderId}/confirm", content: null);
        var firstOrder = await firstResponse.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

        var secondResponse = await _client.PostAsync($"/orders/{orderId}/confirm", content: null);
        var secondOrder = await secondResponse.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(OrderStatus.Confirmed, firstOrder!.Status);
        Assert.Equal(OrderStatus.Confirmed, secondOrder!.Status);
        Assert.Equal(firstOrder.Total, secondOrder.Total);
    }

    [Fact]
    public async Task CancelOrder_TransitionsToCanceled_AndReleasesReservedStock()
    {
        var productId = await SeedProductAsync(unitPrice: 25.00m, availableQuantity: 10);
        var orderId = await CreateOrderAsync(productId, quantity: 4);

        var beforeCancel = await LoadStockAsync(productId);
        Assert.Equal(6, beforeCancel.AvailableQuantity);
        Assert.Equal(4, beforeCancel.ReservedQuantity);

        var response = await _client.PostAsync($"/orders/{orderId}/cancel", content: null);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OrderStatus.Canceled, order!.Status);

        var afterCancel = await LoadStockAsync(productId);
        Assert.Equal(10, afterCancel.AvailableQuantity);
        Assert.Equal(0, afterCancel.ReservedQuantity);

        // Cancelar de novo não pode liberar a mesma reserva duas vezes.
        var secondResponse = await _client.PostAsync($"/orders/{orderId}/cancel", content: null);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var afterSecondCancel = await LoadStockAsync(productId);
        Assert.Equal(10, afterSecondCancel.AvailableQuantity);
        Assert.Equal(0, afterSecondCancel.ReservedQuantity);
    }

    [Fact]
    public async Task GetOrderById_Existing_Returns200()
    {
        var orderId = await CreateOrderAsync(quantity: 1);

        var response = await _client.GetAsync($"/orders/{orderId}");
        var order = await response.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(orderId, order!.Id);
    }

    [Fact]
    public async Task GetOrderById_NonExisting_Returns404WithProblemDetails()
    {
        var response = await _client.GetAsync($"/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.False(string.IsNullOrWhiteSpace(problem!.Title));
        Assert.Equal(404, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Type));
    }

    [Fact]
    public async Task ListOrders_WithCustomerIdFilterAndPagination_ReturnsExpectedEnvelope()
    {
        var customerId = Guid.NewGuid();
        var productId = await SeedProductAsync(unitPrice: 5.00m, availableQuantity: 100);

        for (var i = 0; i < 3; i++)
        {
            await CreateOrderForCustomerAsync(customerId, productId, quantity: 1);
        }

        var response = await _client.GetAsync($"/orders?customerId={customerId}&page=1&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(2, body.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, body.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task ListOrders_OrderSummary_DoesNotIncludeItemsField_WhileGetById_DoesIncludeItems()
    {
        // GET /orders (listagem) não retorna mais os itens de cada pedido,
        // já GET /orders/{id} continua retornando. Aqui checamos
        // explicitamente a AUSÊNCIA da chave "items" na listagem — só
        // conferir id/customerId/status não pega se esse assert for removido
        // por engano em algum refactor.
        var productId = await SeedProductAsync(unitPrice: 15.00m, availableQuantity: 10);
        var customerId = Guid.NewGuid();
        var orderId = await CreateOrderForCustomerAsync(customerId, productId, quantity: 2);

        var listResponse = await _client.GetAsync($"/orders?customerId={customerId}&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var pagedItems = listBody.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(pagedItems);

        var orderSummary = pagedItems[0];

        // Campos esperados do resumo continuam presentes.
        Assert.Equal(orderId.ToString(), orderSummary.GetProperty("id").GetString());
        Assert.Equal(customerId.ToString(), orderSummary.GetProperty("customerId").GetString());
        Assert.True(orderSummary.TryGetProperty("total", out _));
        Assert.True(orderSummary.TryGetProperty("status", out _));
        Assert.True(orderSummary.TryGetProperty("createdAt", out _));

        // Ponto central do teste: o resumo não deve expor "items" (o
        // serializer usa camelCase, então não tem variação de casing a checar).
        Assert.False(
            orderSummary.TryGetProperty("items", out _),
            "GET /orders (listagem) não deveria mais incluir a chave 'items' por pedido.");

        // GET /orders/{id}, em contraste, continua retornando os itens completos.
        var detailResponse = await _client.GetAsync($"/orders/{orderId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var detailBody = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(
            detailBody.TryGetProperty("items", out var detailItems),
            "GET /orders/{id} deveria continuar retornando a chave 'items'.");
        Assert.Equal(JsonValueKind.Array, detailItems.ValueKind);
        Assert.Single(detailItems.EnumerateArray());

        var item = detailItems.EnumerateArray().First();
        Assert.True(item.TryGetProperty("productId", out _));
        Assert.True(item.TryGetProperty("quantity", out _));
    }

    [Fact]
    public async Task ListOrders_WithInvalidStatus_ReturnsProblemDetails()
    {
        var response = await _client.GetAsync("/orders?status=NotAStatus");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal(400, problem!.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Type));
    }

    private async Task<Guid> CreateOrderAsync(int quantity)
    {
        var productId = await SeedProductAsync(unitPrice: 10.00m, availableQuantity: 1000);
        return await CreateOrderAsync(productId, quantity);
    }

    private async Task<Guid> CreateOrderAsync(Guid productId, int quantity)
        => await CreateOrderForCustomerAsync(Guid.NewGuid(), productId, quantity);

    private async Task<Guid> CreateOrderForCustomerAsync(Guid customerId, Guid productId, int quantity)
    {
        var request = new CreateOrderRequest(customerId, Currency.BRL, new[] { new CreateOrderItemRequest(productId, quantity) });
        var response = await _client.PostAsJsonAsync("/orders", request, JsonOptions);
        response.EnsureSuccessStatusCode();

        var order = await response.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
        return order!.Id;
    }

    private async Task<Guid> SeedProductAsync(decimal unitPrice, int availableQuantity)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderServiceDbContext>();

        // O construtor com Id explícito é internal (só Infrastructure/seeder
        // acessa), então aqui usamos o público — o Id é gerado pelo Domain
        // (Guid v7) e lido de volta depois.
        var product = new Product($"Test product {Guid.NewGuid()}", new Money(unitPrice, Currency.BRL));
        context.Products.Add(product);
        context.Stocks.Add(new Stock(product.Id, availableQuantity));
        await context.SaveChangesAsync();

        return product.Id;
    }

    private async Task<Stock> LoadStockAsync(Guid productId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderServiceDbContext>();

        return await context.Stocks.AsNoTracking().SingleAsync(s => s.ProductId == productId);
    }
}
