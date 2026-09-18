using OrderService.Domain.Common;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders;

/// <summary>
/// Projeção de <see cref="Order"/> pra fora da Application — não expomos a entidade de
/// domínio direto pra não vazar comportamento (<c>Confirm</c>/<c>Cancel</c>) nem acoplar a
/// API à forma interna do agregado.
/// </summary>
public sealed record OrderDto(
    Guid Id,
    Guid CustomerId,
    Currency Currency,
    OrderStatus Status,
    decimal Total,
    DateTime CreatedAt,
    IReadOnlyCollection<OrderItemDto> Items)
{
    public static OrderDto FromDomain(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var items = order.Items.Select(OrderItemDto.FromDomain).ToList();

        return new OrderDto(
            order.Id,
            order.CustomerId,
            order.Currency,
            order.Status,
            order.Total.Amount,
            order.CreatedAt,
            items);
    }
}

public sealed record OrderItemDto(Guid ProductId, decimal UnitPrice, Currency Currency, int Quantity, decimal LineTotal)
{
    public static OrderItemDto FromDomain(OrderItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new OrderItemDto(
            item.ProductId,
            item.UnitPrice.Amount,
            item.UnitPrice.Currency,
            item.Quantity,
            item.LineTotal.Amount);
    }
}
