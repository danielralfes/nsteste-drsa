using OrderService.Application.Abstractions;
using OrderService.Domain.Common;
using OrderService.Domain.Orders;
using OrderService.Infrastructure.Persistence.Repositories;
using OrderService.IntegrationTests.TestSupport;

namespace OrderService.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class OrderRepositoryTests
{
    private readonly PostgresFixture _fixture;

    public OrderRepositoryTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsOrderWithItemsAndTotal()
    {
        var customerId = Guid.NewGuid();
        var items = new List<OrderItem>
        {
            new(Guid.NewGuid(), new Money(150.00m, Currency.BRL), 2),
            new(Guid.NewGuid(), new Money(9.90m, Currency.BRL), 3),
        };
        var order = Order.Place(customerId, Currency.BRL, items, DateTime.UtcNow);
        var expectedTotal = order.Total.Amount; // 150*2 + 9.90*3 = 329.70

        await using (var writeContext = _fixture.CreateContext())
        {
            var repository = new OrderRepository(writeContext);
            await repository.AddAsync(order);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = _fixture.CreateContext();
        var readRepository = new OrderRepository(readContext);
        var loaded = await readRepository.GetByIdAsync(order.Id);

        Assert.NotNull(loaded);
        Assert.Equal(order.Id, loaded!.Id);
        Assert.Equal(customerId, loaded.CustomerId);
        Assert.Equal(Currency.BRL, loaded.Currency);
        Assert.Equal(OrderStatus.Placed, loaded.Status);
        Assert.Equal(2, loaded.Items.Count);
        Assert.Equal(expectedTotal, loaded.Total.Amount);
        Assert.Equal(329.70m, expectedTotal);

        var loadedProductIds = loaded.Items.Select(i => i.ProductId).ToHashSet();
        var originalProductIds = items.Select(i => i.ProductId).ToHashSet();
        Assert.Equal(originalProductIds, loadedProductIds);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsTrackedEntity_SoConfirmPersistsAfterSaveChanges()
    {
        // GetByIdAsync não pode usar AsNoTracking, senão Confirm()/Cancel()
        // seguido de SaveChangesAsync vira um no-op silencioso.
        var order = Order.Place(
            Guid.NewGuid(),
            Currency.BRL,
            new List<OrderItem> { new(Guid.NewGuid(), new Money(10m, Currency.BRL), 1) },
            DateTime.UtcNow);

        await using (var writeContext = _fixture.CreateContext())
        {
            await new OrderRepository(writeContext).AddAsync(order);
            await writeContext.SaveChangesAsync();
        }

        await using (var confirmContext = _fixture.CreateContext())
        {
            var repository = new OrderRepository(confirmContext);
            var tracked = await repository.GetByIdAsync(order.Id);
            tracked!.Confirm();
            await confirmContext.SaveChangesAsync();
        }

        await using var verifyContext = _fixture.CreateContext();
        var reloaded = await new OrderRepository(verifyContext).GetByIdAsync(order.Id);
        Assert.Equal(OrderStatus.Confirmed, reloaded!.Status);
    }

    [Fact]
    public async Task ListAsync_FiltersByCustomerIdAndPaginatesCorrectly()
    {
        var customerId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();

        await using (var writeContext = _fixture.CreateContext())
        {
            var repository = new OrderRepository(writeContext);

            for (var i = 0; i < 5; i++)
            {
                var order = Order.Place(
                    customerId,
                    Currency.BRL,
                    new List<OrderItem> { new(Guid.NewGuid(), new Money(10m, Currency.BRL), 1) },
                    DateTime.UtcNow.AddMinutes(-i));
                await repository.AddAsync(order);
            }

            var otherOrder = Order.Place(
                otherCustomerId,
                Currency.BRL,
                new List<OrderItem> { new(Guid.NewGuid(), new Money(10m, Currency.BRL), 1) },
                DateTime.UtcNow);
            await repository.AddAsync(otherOrder);

            await writeContext.SaveChangesAsync();
        }

        await using var readContext = _fixture.CreateContext();
        var readRepository = new OrderRepository(readContext);

        var filter = new OrderListFilter(customerId, null, null, null, Page: 1, PageSize: 2);
        var page1 = await readRepository.ListAsync(filter);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.All(page1.Items, o => Assert.Equal(customerId, o.CustomerId));

        var page3 = await readRepository.ListAsync(filter with { Page = 3 });
        Assert.Single(page3.Items);
    }

    [Fact]
    public async Task ListAsync_OrderWithMultipleItems_ReturnsSingleSummaryRowWithCorrectTotal()
    {
        // ListAsync projeta direto para OrderSummaryDto sem Include(Items),
        // a listagem não expõe mais os itens do pedido. Aqui confirmamos que,
        // mesmo com vários itens, a página traz exatamente uma linha de
        // resumo por pedido, com o total certo — sem duplicação de um JOIN
        // com a tabela filha, que nem existe mais nessa query.
        var customerId = Guid.NewGuid();
        var items = new List<OrderItem>
        {
            new(Guid.NewGuid(), new Money(10m, Currency.BRL), 1),
            new(Guid.NewGuid(), new Money(20m, Currency.BRL), 2),
            new(Guid.NewGuid(), new Money(30m, Currency.BRL), 3),
            new(Guid.NewGuid(), new Money(40m, Currency.BRL), 4),
        };
        var order = Order.Place(customerId, Currency.BRL, items, DateTime.UtcNow);
        var expectedTotal = order.Total.Amount;

        await using (var writeContext = _fixture.CreateContext())
        {
            var repository = new OrderRepository(writeContext);
            await repository.AddAsync(order);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = _fixture.CreateContext();
        var readRepository = new OrderRepository(readContext);

        var filter = new OrderListFilter(customerId, null, null, null, Page: 1, PageSize: 20);
        var result = await readRepository.ListAsync(filter);

        // Exatamente uma linha de resumo na página, mesmo com 4 itens no pedido.
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);

        var loaded = result.Items.Single();
        Assert.Equal(order.Id, loaded.Id);
        Assert.Equal(expectedTotal, loaded.Total);
        Assert.Equal(OrderStatus.Placed, loaded.Status);
    }

    [Fact]
    public async Task ListAsync_WithFivePagedOrders_ExecutesExactlyTwoQueries()
    {
        // Prova de ausência de N+1: listar N pedidos com itens deve disparar
        // uma quantidade fixa e pequena de queries (COUNT + SELECT paginado),
        // independente de N ou de quantos itens cada pedido tem.
        var customerId = Guid.NewGuid();

        await using (var writeContext = _fixture.CreateContext())
        {
            var repository = new OrderRepository(writeContext);

            for (var i = 0; i < 5; i++)
            {
                var order = Order.Place(
                    customerId,
                    Currency.BRL,
                    new List<OrderItem>
                    {
                        new(Guid.NewGuid(), new Money(10m, Currency.BRL), 1),
                        new(Guid.NewGuid(), new Money(20m, Currency.BRL), 2),
                    },
                    DateTime.UtcNow.AddMinutes(-i));
                await repository.AddAsync(order);
            }

            await writeContext.SaveChangesAsync();
        }

        var counter = new QueryCountingInterceptor();
        await using var readContext = _fixture.CreateContext(counter);
        var readRepository = new OrderRepository(readContext);

        var filter = new OrderListFilter(customerId, null, null, null, Page: 1, PageSize: 20);
        var result = await readRepository.ListAsync(filter);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(5, result.Items.Count);

        // Sempre 2 queries: COUNT + SELECT paginado, nunca proporcional ao
        // número de pedidos retornados.
        Assert.Equal(2, counter.QueryCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public async Task ListAsync_QueryCountIsConstant_RegardlessOfNumberOfOrdersReturned(int orderCount)
    {
        // Complementa ListAsync_WithFivePagedOrders_ExecutesExactlyTwoQueries:
        // aquele teste prova 2 queries pra N=5, mas sozinho não prova que o
        // número de queries é constante (O(1)) em vez de proporcional a N —
        // só variando N dá pra distinguir as duas hipóteses. pageSize é
        // grande o bastante pra trazer todos os pedidos numa única página,
        // então orderCount pedidos aparecem de fato no resultado.
        var customerId = Guid.NewGuid();

        await using (var writeContext = _fixture.CreateContext())
        {
            var repository = new OrderRepository(writeContext);

            for (var i = 0; i < orderCount; i++)
            {
                var order = Order.Place(
                    customerId,
                    Currency.BRL,
                    new List<OrderItem>
                    {
                        new(Guid.NewGuid(), new Money(10m, Currency.BRL), 1),
                        new(Guid.NewGuid(), new Money(20m, Currency.BRL), 2),
                        new(Guid.NewGuid(), new Money(30m, Currency.BRL), 3),
                    },
                    DateTime.UtcNow.AddMinutes(-i));
                await repository.AddAsync(order);
            }

            await writeContext.SaveChangesAsync();
        }

        var counter = new QueryCountingInterceptor();
        await using var readContext = _fixture.CreateContext(counter);
        var readRepository = new OrderRepository(readContext);

        var filter = new OrderListFilter(customerId, null, null, null, Page: 1, PageSize: 50);
        var result = await readRepository.ListAsync(filter);

        Assert.Equal(orderCount, result.TotalCount);
        Assert.Equal(orderCount, result.Items.Count);

        // 2 queries independente de orderCount ser 1, 3 ou 10 — se fosse
        // O(N), esse assert falharia pra N > 1.
        Assert.Equal(2, counter.QueryCount);
    }
}
