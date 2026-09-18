using OrderService.Domain.Common;

namespace OrderService.Domain.Tests.Common;

public class MoneyTests
{
    [Fact]
    public void Add_WithSameCurrency_SumsAmounts()
    {
        var a = new Money(10.50m, Currency.BRL);
        var b = new Money(4.25m, Currency.BRL);

        var result = a.Add(b);

        Assert.Equal(14.75m, result.Amount);
        Assert.Equal(Currency.BRL, result.Currency);
    }

    [Fact]
    public void Add_WithDifferentCurrency_ThrowsCurrencyMismatchException()
    {
        var brl = new Money(10m, Currency.BRL);
        var usd = new Money(10m, Currency.USD);

        var exception = Assert.Throws<CurrencyMismatchException>(() => brl.Add(usd));

        Assert.Equal(Currency.BRL, exception.Expected);
        Assert.Equal(Currency.USD, exception.Actual);
    }

    [Theory]
    [InlineData(2.005, 2.01)]
    [InlineData(2.015, 2.02)]
    [InlineData(-2.005, -2.01)]
    public void Constructor_RoundsAwayFromZero_OnMidpoint(decimal input, decimal expected)
    {
        var money = new Money(input, Currency.BRL);

        Assert.Equal(expected, money.Amount);
    }

    [Fact]
    public void Zero_CreatesMoneyWithAmountZero()
    {
        var money = Money.Zero(Currency.EUR);

        Assert.Equal(0m, money.Amount);
        Assert.Equal(Currency.EUR, money.Currency);
    }

    [Fact]
    public void Multiply_ScalesAmountAndKeepsCurrency()
    {
        var money = new Money(9.99m, Currency.USD);

        var result = money.Multiply(3);

        Assert.Equal(29.97m, result.Amount);
        Assert.Equal(Currency.USD, result.Currency);
    }

    // Money é um value object genérico e continua permissivo a valores negativos
    // (útil pra deltas/diferenças). Quem precisa vetar negativos faz isso no
    // próprio contexto — ver Product, que valida UnitPrice.Amount >= 0 (decisions.md seção 17).
    [Fact]
    public void Constructor_WithNegativeAmount_DoesNotThrow()
    {
        var money = new Money(-10.00m, Currency.BRL);

        Assert.Equal(-10.00m, money.Amount);
    }

    [Fact]
    public void Add_WithNegativeAndPositiveAmounts_SumsCorrectly()
    {
        var a = new Money(-5.00m, Currency.BRL);
        var b = new Money(10.00m, Currency.BRL);

        var result = a.Add(b);

        Assert.Equal(5.00m, result.Amount);
    }
}
