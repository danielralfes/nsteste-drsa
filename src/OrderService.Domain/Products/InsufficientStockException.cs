using OrderService.Domain.Common;

namespace OrderService.Domain.Products;

/// <summary>
/// Representa "estoque insuficiente pra reserva". <b><see cref="Stock"/> não
/// lança isso</b> — <see cref="Stock.TryReserve"/> segue o padrão <c>Try*</c>
/// e só retorna <c>false</c>. Quem lança é a Application layer (Fatia 2/3),
/// convertendo o <c>false</c> num erro de domínio explícito (decisions.md seção 18).
/// </summary>
public sealed class InsufficientStockException : DomainException
{
    public InsufficientStockException(Guid productId, int requestedQuantity, int availableQuantity)
        : base(
            $"Insufficient stock for product '{productId}': requested '{requestedQuantity}', " +
            $"available '{availableQuantity}'.")
    {
        ProductId = productId;
        RequestedQuantity = requestedQuantity;
        AvailableQuantity = availableQuantity;
    }

    public Guid ProductId { get; }

    public int RequestedQuantity { get; }

    public int AvailableQuantity { get; }
}
