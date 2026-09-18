using OrderService.Domain.Common;

namespace OrderService.Domain.Orders;

/// <summary>
/// Linha de um <see cref="Order"/>. <see cref="UnitPrice"/> é um snapshot do
/// preço no momento da compra — por isso não guardamos referência direta a
/// <see cref="Products.Product"/>, senão o pedido mudaria de valor se o
/// preço do produto mudasse depois.
/// </summary>
public sealed class OrderItem
{
    /// <summary>
    /// Construtor vazio pro EF Core (Fatia 3): ele não vincula parâmetros de
    /// construtor a propriedades de tipo complexo (<c>unitPrice</c>, mapeado
    /// como Complex Type), então precisa dessa via de reflection. Preenchido
    /// pelo EF logo depois; código de domínio/aplicação nunca chama isso.
    /// </summary>
    private OrderItem()
    {
    }

    public OrderItem(Guid productId, Money unitPrice, int quantity)
    {
        if (quantity <= 0)
        {
            throw new InvalidQuantityException(quantity);
        }

        Id = Guid.CreateVersion7();
        ProductId = productId;
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public Guid Id { get; }

    public Guid ProductId { get; }

    public Money UnitPrice { get; }

    public int Quantity { get; }

    /// <summary>Total da linha: <see cref="UnitPrice"/> * <see cref="Quantity"/>.</summary>
    public Money LineTotal => UnitPrice.Multiply(Quantity);
}
