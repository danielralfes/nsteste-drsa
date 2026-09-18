namespace OrderService.Domain.Common;

/// <summary>
/// Lançada quando uma quantidade viola a invariante esperada — seja uma
/// quantidade que precisa ser estritamente positiva (item de pedido, reserva
/// de estoque) ou um contador que só não pode ser negativo
/// (<see cref="Products.Stock.AvailableQuantity"/>,
/// <see cref="Products.Stock.ReservedQuantity"/>).
/// </summary>
public sealed class InvalidQuantityException : DomainException
{
    /// <summary>Caso mais comum: quantidade precisa ser estritamente positiva.</summary>
    public InvalidQuantityException(int quantity)
        : this(quantity, "greater than zero")
    {
    }

    /// <summary>
    /// Permite descrever a invariante violada quando não é "maior que zero"
    /// (ex.: "greater than or equal to zero" pros contadores de estoque).
    /// </summary>
    public InvalidQuantityException(int quantity, string requirement)
        : base($"Quantity must be {requirement}, but was '{quantity}'.")
    {
        Quantity = quantity;
    }

    public int Quantity { get; }
}
