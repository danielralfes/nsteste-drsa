using OrderService.Domain.Orders;

namespace OrderService.Application.Orders.ListOrders;

/// <summary>
/// Filtros de <c>GET /orders</c>. <see cref="Page"/>/<see cref="PageSize"/> nulos assumem
/// os defaults (page 1, pageSize 20); fora do intervalo permitido são rejeitados por
/// <see cref="ListOrdersHandler"/> com <see cref="Common.ValidationException"/>.
/// <see cref="Status"/> já chega como enum — parsing de string pra enum (e o 400 se vier
/// inválido) é responsabilidade da camada de API.
/// </summary>
public sealed record ListOrdersQuery(
    Guid? CustomerId,
    OrderStatus? Status,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int? Page,
    int? PageSize);
