namespace OrderService.Application.Common;

/// <summary>O pedido identificado por <see cref="OrderId"/> não existe.</summary>
/// <remarks>
/// <see cref="OrderId"/> é o Id do recurso endereçado direto na URL (ex.:
/// <c>GET/POST /orders/{id}/...</c>). Mapeamento HTTP sugerido: <c>404 Not Found</c>.
/// </remarks>
public sealed class OrderNotFoundException : NotFoundException
{
    public OrderNotFoundException(Guid orderId)
        : base($"Order '{orderId}' was not found.")
    {
        OrderId = orderId;
    }

    public Guid OrderId { get; }
}
