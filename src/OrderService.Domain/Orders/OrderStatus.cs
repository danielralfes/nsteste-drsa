namespace OrderService.Domain.Orders;

/// <summary>
/// Estado do pedido. <see cref="Draft"/> está aqui por fidelidade ao modelo
/// do README, mas nenhum fluxo público alcança esse estado na v1 — todo
/// pedido nasce direto em <see cref="Placed"/> (decisions.md seção 13).
/// </summary>
public enum OrderStatus
{
    Draft,
    Placed,
    Confirmed,
    Canceled,
}
