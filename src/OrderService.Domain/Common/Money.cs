namespace OrderService.Domain.Common;

/// <summary>Value object monetário imutável: um <see cref="Amount"/> com sua <see cref="Currency"/>.</summary>
/// <remarks>
/// Sempre arredonda para 2 casas na construção, com <see cref="MidpointRounding.AwayFromZero"/>
/// em vez do banker's rounding padrão do .NET (decisions.md seção 10).
/// </remarks>
public readonly record struct Money
{
    private const int DecimalPrecision = 2;

    public Money(decimal amount, Currency currency)
    {
        Amount = Math.Round(amount, DecimalPrecision, MidpointRounding.AwayFromZero);
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public static Money Zero(Currency currency) => new(0m, currency);

    /// <summary>Soma dois valores. Lança <see cref="CurrencyMismatchException"/> se as currencies divergirem.</summary>
    public Money Add(Money other)
    {
        if (Currency != other.Currency)
        {
            throw new CurrencyMismatchException(Currency, other.Currency);
        }

        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>Multiplica pela quantidade, preservando a currency. Usado no total de linha do pedido.</summary>
    public Money Multiply(int quantity) => new(Amount * quantity, Currency);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
