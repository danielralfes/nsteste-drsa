using OrderService.Domain.Common;

namespace OrderService.Api.Contracts;

/// <summary>
/// Payload de <c>POST /orders</c>. Separado de
/// <see cref="OrderService.Application.Orders.CreateOrder.CreateOrderCommand"/>
/// de propósito, para não acoplar a assinatura HTTP ao contrato interno da
/// Application — dá margem pra versionar a API sem mexer em Application.
/// </summary>
public sealed record CreateOrderRequest(Guid CustomerId, Currency Currency, IReadOnlyCollection<CreateOrderItemRequest>? Items);

public sealed record CreateOrderItemRequest(Guid ProductId, int Quantity);
