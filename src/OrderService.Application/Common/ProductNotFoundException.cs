namespace OrderService.Application.Common;

/// <summary>Um produto referenciado no payload de criação de pedido não existe.</summary>
/// <remarks>
/// Diferente de <see cref="OrderNotFoundException"/>: aqui o Id inexistente é dado de
/// entrada no corpo do <c>POST /orders</c>, não o recurso endereçado pela URL. Mapeamento
/// sugerido: <c>422 Unprocessable Entity</c>, mesma família das outras rejeições de
/// <see cref="Orders.Order.Place"/> (payload sintaticamente válido, mas referenciando algo
/// que não existe no catálogo) — não <c>404</c>, que é só quando o Id inexistente é o
/// próprio recurso da URL.
/// </remarks>
public sealed class ProductNotFoundException : NotFoundException
{
    public ProductNotFoundException(Guid productId)
        : base($"Product '{productId}' was not found.")
    {
        ProductId = productId;
    }

    public Guid ProductId { get; }
}
