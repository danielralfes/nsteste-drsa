using OrderService.Domain.Common;
using OrderService.Domain.Products;

namespace OrderService.Domain.Tests.Products;

public class ProductTests
{
    [Fact]
    public void Constructor_GeneratesNonEmptyId()
    {
        var product = new Product("Widget", new Money(9.90m, Currency.BRL));

        Assert.NotEqual(Guid.Empty, product.Id);
    }

    [Fact]
    public void Constructor_GeneratesDifferentIdsForEachInstance()
    {
        var first = new Product("Widget", new Money(9.90m, Currency.BRL));
        var second = new Product("Gadget", new Money(19.90m, Currency.BRL));

        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyName_ThrowsInvalidProductNameException(string name)
    {
        Assert.Throws<InvalidProductNameException>(() => new Product(name, new Money(1m, Currency.BRL)));
    }

    // UnitPrice não pode ser negativo, e isso é garantido no construtor do
    // Product independente de quem o chama (decisions.md seção 17).
    [Fact]
    public void Constructor_WithNegativeUnitPrice_ThrowsInvalidPriceException()
    {
        var exception = Assert.Throws<InvalidPriceException>(
            () => new Product("Widget", new Money(-1.00m, Currency.BRL)));

        Assert.Equal(-1.00m, exception.Amount);
    }
}
