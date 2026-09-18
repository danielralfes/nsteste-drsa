using OrderService.Domain.Common;

namespace OrderService.Domain.Products;

/// <summary>Lançada quando <see cref="Product.UnitPrice"/> é negativo (decisions.md seção 17).</summary>
public sealed class InvalidPriceException : DomainException
{
    public InvalidPriceException(decimal amount)
        : base($"Price must not be negative, but was '{amount}'.")
    {
        Amount = amount;
    }

    public decimal Amount { get; }
}
