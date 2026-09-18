using OrderService.Domain.Common;
using OrderService.Domain.Orders;

namespace OrderService.Domain.Tests.Orders;

public class OrderTests
{
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly DateTime FixedCreatedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Place_WithValidItems_CalculatesTotalAndStartsAsPlaced()
    {
        var items = new[]
        {
            new OrderItem(Guid.NewGuid(), new Money(10.00m, Currency.BRL), 2),
            new OrderItem(Guid.NewGuid(), new Money(5.50m, Currency.BRL), 3),
        };

        var order = Order.Place(CustomerId, Currency.BRL, items, DateTime.UtcNow);

        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(36.50m, order.Total.Amount);
        Assert.Equal(Currency.BRL, order.Total.Currency);
        Assert.Equal(2, order.Items.Count);
    }

    [Fact]
    public void Place_WithCreatedAtUtc_SetsCreatedAtToProvidedValue()
    {
        var items = new[]
        {
            new OrderItem(Guid.NewGuid(), new Money(10.00m, Currency.BRL), 1),
        };

        var order = Order.Place(CustomerId, Currency.BRL, items, FixedCreatedAtUtc);

        Assert.Equal(FixedCreatedAtUtc, order.CreatedAt);
    }

    [Fact]
    public void Place_WithNoItems_ThrowsEmptyOrderException()
    {
        Assert.Throws<EmptyOrderException>(() => Order.Place(CustomerId, Currency.BRL, Array.Empty<OrderItem>(), DateTime.UtcNow));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OrderItem_WithNonPositiveQuantity_ThrowsInvalidQuantityException(int quantity)
    {
        Assert.Throws<InvalidQuantityException>(
            () => new OrderItem(Guid.NewGuid(), new Money(10m, Currency.BRL), quantity));
    }

    [Fact]
    public void Place_WithItemCurrencyDifferentFromOrderCurrency_ThrowsCurrencyMismatchException()
    {
        var items = new[]
        {
            new OrderItem(Guid.NewGuid(), new Money(10.00m, Currency.USD), 1),
        };

        var exception = Assert.Throws<CurrencyMismatchException>(
            () => Order.Place(CustomerId, Currency.BRL, items, DateTime.UtcNow));

        Assert.Equal(Currency.BRL, exception.Expected);
        Assert.Equal(Currency.USD, exception.Actual);
    }

    [Fact]
    public void Confirm_FromPlaced_TransitionsToConfirmed()
    {
        var order = CreatePlacedOrder();

        order.Confirm();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Confirm_TwiceFromPlaced_IsIdempotent()
    {
        var order = CreatePlacedOrder();

        order.Confirm();
        order.Confirm();

        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void Confirm_FromCanceled_ThrowsInvalidOrderStateTransitionException()
    {
        var order = CreatePlacedOrder();
        order.Cancel();

        Assert.Throws<InvalidOrderStateTransitionException>(() => order.Confirm());
    }

    [Fact]
    public void Cancel_FromPlaced_TransitionsToCanceled()
    {
        var order = CreatePlacedOrder();

        var result = order.Cancel();

        Assert.True(result);
        Assert.Equal(OrderStatus.Canceled, order.Status);
    }

    [Fact]
    public void Cancel_FromConfirmed_TransitionsToCanceled()
    {
        var order = CreatePlacedOrder();
        order.Confirm();

        var result = order.Cancel();

        Assert.True(result);
        Assert.Equal(OrderStatus.Canceled, order.Status);
    }

    [Fact]
    public void Cancel_TwiceFromCanceled_IsIdempotent()
    {
        var order = CreatePlacedOrder();
        order.Cancel();

        var result = order.Cancel();

        Assert.False(result);
        Assert.Equal(OrderStatus.Canceled, order.Status);
    }

    [Fact]
    public void Confirm_AfterCancel_ThrowsInvalidOrderStateTransitionException()
    {
        var order = CreatePlacedOrder();
        order.Cancel();

        var exception = Assert.Throws<InvalidOrderStateTransitionException>(() => order.Confirm());

        Assert.Equal(OrderStatus.Canceled, exception.CurrentStatus);
        Assert.Equal(OrderStatus.Confirmed, exception.AttemptedStatus);
    }

    [Fact]
    public void Place_GeneratesNonEmptyId()
    {
        var order = CreatePlacedOrder();

        Assert.NotEqual(Guid.Empty, order.Id);
    }

    [Fact]
    public void Place_GeneratesDifferentIdsForEachOrder()
    {
        var first = CreatePlacedOrder();
        var second = CreatePlacedOrder();

        Assert.NotEqual(first.Id, second.Id);
    }

    // Itens com o mesmo ProductId no payload de Place são rejeitados, não
    // consolidados nem aceitos como linhas separadas (decisions.md seção 16).
    [Fact]
    public void Place_WithDuplicateProductIdAcrossItems_ThrowsDuplicateProductInOrderException()
    {
        var productId = Guid.NewGuid();
        var items = new[]
        {
            new OrderItem(productId, new Money(10.00m, Currency.BRL), 2),
            new OrderItem(productId, new Money(10.00m, Currency.BRL), 3),
        };

        var exception = Assert.Throws<DuplicateProductInOrderException>(
            () => Order.Place(CustomerId, Currency.BRL, items, DateTime.UtcNow));

        Assert.Equal(productId, exception.ProductId);
    }

    [Fact]
    public void Total_WithThreeOrMoreDifferentItems_SumsAllLineTotalsCorrectly()
    {
        var items = new[]
        {
            new OrderItem(Guid.NewGuid(), new Money(10.33m, Currency.BRL), 3),
            new OrderItem(Guid.NewGuid(), new Money(7.99m, Currency.BRL), 2),
            new OrderItem(Guid.NewGuid(), new Money(1.01m, Currency.BRL), 5),
            new OrderItem(Guid.NewGuid(), new Money(100.00m, Currency.BRL), 1),
        };

        var order = Order.Place(CustomerId, Currency.BRL, items, DateTime.UtcNow);

        // 10.33*3 + 7.99*2 + 1.01*5 + 100.00*1 = 30.99 + 15.98 + 5.05 + 100.00 = 152.02
        Assert.Equal(152.02m, order.Total.Amount);
    }

    private static Order CreatePlacedOrder()
    {
        var items = new[]
        {
            new OrderItem(Guid.NewGuid(), new Money(10.00m, Currency.BRL), 1),
        };

        return Order.Place(CustomerId, Currency.BRL, items, DateTime.UtcNow);
    }
}
