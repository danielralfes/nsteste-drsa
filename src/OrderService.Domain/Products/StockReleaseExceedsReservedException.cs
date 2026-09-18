using OrderService.Domain.Common;

namespace OrderService.Domain.Products;

/// <summary>
/// Lançada quando <see cref="Stock.Release"/> tenta devolver mais unidades do
/// que existe em <see cref="Stock.ReservedQuantity"/>. Diferente de estoque
/// insuficiente (situação normal de negócio), isso é violação de invariante
/// interno — não deveria acontecer se a Application layer sempre libera
/// exatamente o que reservou.
/// </summary>
public sealed class StockReleaseExceedsReservedException : DomainException
{
    public StockReleaseExceedsReservedException(Guid productId, int requestedQuantity, int reservedQuantity)
        : base(
            $"Cannot release '{requestedQuantity}' units of product '{productId}': " +
            $"only '{reservedQuantity}' are currently reserved.")
    {
        ProductId = productId;
        RequestedQuantity = requestedQuantity;
        ReservedQuantity = reservedQuantity;
    }

    public Guid ProductId { get; }

    public int RequestedQuantity { get; }

    public int ReservedQuantity { get; }
}
