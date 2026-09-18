using OrderService.Domain.Orders;

namespace OrderService.Application.Abstractions;

/// <summary>
/// Filtro + paginação para <see cref="IOrderRepository.ListAsync"/>. <see cref="FromUtc"/>
/// e <see cref="ToUtc"/> são inclusivos sobre <see cref="Order.CreatedAt"/>.
/// <see cref="Page"/> e <see cref="PageSize"/> já chegam validados pelo handler de
/// <c>ListOrders</c> — 1-based, <see cref="PageSize"/> dentro do limite máximo.
/// </summary>
public sealed record OrderListFilter(
    Guid? CustomerId,
    OrderStatus? Status,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page,
    int PageSize);
