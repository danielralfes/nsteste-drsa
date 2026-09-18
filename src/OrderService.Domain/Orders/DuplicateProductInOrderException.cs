using OrderService.Domain.Common;

namespace OrderService.Domain.Orders;

/// <summary>
/// Lançada quando <see cref="Order.Place"/> recebe dois ou mais itens com o
/// mesmo <see cref="OrderItem.ProductId"/>. A duplicata não é consolidada nem
/// vira linhas independentes — é tratada como payload malformado (decisions.md seção 16).
/// </summary>
public sealed class DuplicateProductInOrderException : DomainException
{
    public DuplicateProductInOrderException(Guid productId)
        : base($"Order payload contains duplicate items for product '{productId}'.")
    {
        ProductId = productId;
    }

    public Guid ProductId { get; }
}
