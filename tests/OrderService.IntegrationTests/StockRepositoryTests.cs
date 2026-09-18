using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Products;
using OrderService.Infrastructure.Persistence.Repositories;
using OrderService.IntegrationTests.TestSupport;

namespace OrderService.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class StockRepositoryTests
{
    private readonly PostgresFixture _fixture;

    public StockRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TryReserveAsync_WithSufficientStock_ReservesAndUpdatesCounters()
    {
        var productId = Guid.NewGuid();
        await SeedStockAsync(productId, available: 10, reserved: 0);

        await using var context = _fixture.CreateContext();
        var repository = new StockRepository(context);

        var result = await repository.TryReserveAsync(productId, 4);

        Assert.True(result.Success);

        var stock = await LoadStockAsync(productId);
        Assert.Equal(6, stock.AvailableQuantity);
        Assert.Equal(4, stock.ReservedQuantity);
    }

    [Fact]
    public async Task TryReserveAsync_WithInsufficientStock_FailsWithoutSideEffects()
    {
        var productId = Guid.NewGuid();
        await SeedStockAsync(productId, available: 3, reserved: 0);

        await using var context = _fixture.CreateContext();
        var repository = new StockRepository(context);

        var result = await repository.TryReserveAsync(productId, 5);

        Assert.False(result.Success);
        Assert.Equal(3, result.AvailableQuantity);

        // Sem efeito colateral: os contadores no banco continuam exatamente
        // como estavam antes da tentativa.
        var stock = await LoadStockAsync(productId);
        Assert.Equal(3, stock.AvailableQuantity);
        Assert.Equal(0, stock.ReservedQuantity);
    }

    [Fact]
    public async Task TryReserveAsync_ConcurrentCallsForSameProduct_OnlyOneSucceeds()
    {
        // Duas reservas concorrentes de 5 unidades cada sobre um total de 5
        // disponíveis — só uma pode ganhar. Cada chamada usa seu próprio
        // DbContext/conexão (DbContext não é thread-safe); quem garante a
        // exclusão mútua de fato é o UPDATE condicional no banco, não o .NET.
        var productId = Guid.NewGuid();
        await SeedStockAsync(productId, available: 5, reserved: 0);

        await using var contextA = _fixture.CreateContext();
        await using var contextB = _fixture.CreateContext();
        var repositoryA = new StockRepository(contextA);
        var repositoryB = new StockRepository(contextB);

        var taskA = repositoryA.TryReserveAsync(productId, 5);
        var taskB = repositoryB.TryReserveAsync(productId, 5);

        var results = await Task.WhenAll(taskA, taskB);

        var successCount = results.Count(r => r.Success);
        Assert.Equal(1, successCount);

        var stock = await LoadStockAsync(productId);
        Assert.Equal(0, stock.AvailableQuantity);
        Assert.Equal(5, stock.ReservedQuantity);
    }

    [Fact]
    public async Task ReleaseAsync_MovesQuantityBackFromReservedToAvailable()
    {
        var productId = Guid.NewGuid();
        await SeedStockAsync(productId, available: 2, reserved: 3);

        await using var context = _fixture.CreateContext();
        var repository = new StockRepository(context);

        await repository.ReleaseAsync(productId, 3);

        var stock = await LoadStockAsync(productId);
        Assert.Equal(5, stock.AvailableQuantity);
        Assert.Equal(0, stock.ReservedQuantity);
    }

    private async Task SeedStockAsync(Guid productId, int available, int reserved)
    {
        await using var context = _fixture.CreateContext();
        context.Stocks.Add(new Stock(productId, available, reserved));
        await context.SaveChangesAsync();
    }

    private async Task<Stock> LoadStockAsync(Guid productId)
    {
        await using var context = _fixture.CreateContext();
        return await context.Stocks.AsNoTracking().SingleAsync(s => s.ProductId == productId);
    }
}
