using OrderService.Domain.Common;
using OrderService.Domain.Products;

namespace OrderService.Domain.Tests.Products;

public class StockTests
{
    [Fact]
    public void TryReserve_WithSufficientStock_MovesAvailableToReserved()
    {
        var stock = new Stock(Guid.NewGuid(), availableQuantity: 10);

        var reserved = stock.TryReserve(4);

        Assert.True(reserved);
        Assert.Equal(6, stock.AvailableQuantity);
        Assert.Equal(4, stock.ReservedQuantity);
    }

    [Fact]
    public void TryReserve_WithInsufficientStock_FailsAndLeavesCountersUnchanged()
    {
        var stock = new Stock(Guid.NewGuid(), availableQuantity: 3);

        var reserved = stock.TryReserve(4);

        Assert.False(reserved);
        Assert.Equal(3, stock.AvailableQuantity);
        Assert.Equal(0, stock.ReservedQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryReserve_WithNonPositiveQuantity_ThrowsInvalidQuantityException(int quantity)
    {
        var stock = new Stock(Guid.NewGuid(), availableQuantity: 10);

        Assert.Throws<InvalidQuantityException>(() => stock.TryReserve(quantity));
    }

    [Fact]
    public void Release_MovesReservedBackToAvailable()
    {
        var stock = new Stock(Guid.NewGuid(), availableQuantity: 10);
        stock.TryReserve(6);

        stock.Release(4);

        Assert.Equal(8, stock.AvailableQuantity);
        Assert.Equal(2, stock.ReservedQuantity);
    }

    [Fact]
    public void Release_MoreThanReserved_ThrowsStockReleaseExceedsReservedException()
    {
        var stock = new Stock(Guid.NewGuid(), availableQuantity: 10);
        stock.TryReserve(2);

        Assert.Throws<StockReleaseExceedsReservedException>(() => stock.Release(3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Release_WithNonPositiveQuantity_ThrowsInvalidQuantityException(int quantity)
    {
        var stock = new Stock(Guid.NewGuid(), availableQuantity: 10, reservedQuantity: 5);

        Assert.Throws<InvalidQuantityException>(() => stock.Release(quantity));
    }

    [Fact]
    public void Constructor_WithNegativeAvailableQuantity_ThrowsInvalidQuantityException()
    {
        var exception = Assert.Throws<InvalidQuantityException>(() => new Stock(Guid.NewGuid(), availableQuantity: -1));

        Assert.Equal(-1, exception.Quantity);
    }

    [Fact]
    public void Constructor_WithNegativeReservedQuantity_ThrowsInvalidQuantityException()
    {
        var exception = Assert.Throws<InvalidQuantityException>(
            () => new Stock(Guid.NewGuid(), availableQuantity: 0, reservedQuantity: -1));

        Assert.Equal(-1, exception.Quantity);
    }

    [Fact]
    public void TryReserve_WithQuantityExactlyEqualToAvailable_SucceedsAndZeroesAvailable()
    {
        var stock = new Stock(Guid.NewGuid(), availableQuantity: 5);

        var reserved = stock.TryReserve(5);

        Assert.True(reserved);
        Assert.Equal(0, stock.AvailableQuantity);
        Assert.Equal(5, stock.ReservedQuantity);
    }

    [Fact]
    public void Release_WithQuantityExactlyEqualToReserved_SucceedsAndZeroesReserved()
    {
        var stock = new Stock(Guid.NewGuid(), availableQuantity: 10);
        stock.TryReserve(4);

        stock.Release(4);

        Assert.Equal(0, stock.ReservedQuantity);
        Assert.Equal(10, stock.AvailableQuantity);
    }
}
