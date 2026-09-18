namespace OrderService.Domain.Common;

/// <summary>
/// Lançada quando uma operação mistura currencies onde só uma é esperada —
/// somar dois <see cref="Money"/>, comparar pedido e item, etc.
/// </summary>
public sealed class CurrencyMismatchException : DomainException
{
    public CurrencyMismatchException(Currency expected, Currency actual)
        : base($"Currency mismatch: expected '{expected}' but got '{actual}'.")
    {
        Expected = expected;
        Actual = actual;
    }

    public Currency Expected { get; }

    public Currency Actual { get; }
}
