using OrderService.Domain.Common;

namespace OrderService.Domain.Orders;

/// <summary>
/// Lançada ao tentar criar um <see cref="Order"/> sem itens.
/// </summary>
public sealed class EmptyOrderException : DomainException
{
    public EmptyOrderException()
        : base("An order must have at least one item.")
    {
    }
}
