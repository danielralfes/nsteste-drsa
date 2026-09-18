using OrderService.Application.Abstractions;
using OrderService.Application.Common;

namespace OrderService.Application.Orders.ListOrders;

/// <summary>Caso de uso <c>GET /orders</c>.</summary>
public sealed class ListOrdersHandler
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly IOrderRepository _orderRepository;

    public ListOrdersHandler(IOrderRepository orderRepository)
    {
        ArgumentNullException.ThrowIfNull(orderRepository);

        _orderRepository = orderRepository;
    }

    public async Task<PagedResult<OrderSummaryDto>> HandleAsync(ListOrdersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = query.Page ?? 1;
        if (page < 1)
        {
            throw new ValidationException("page must be greater than or equal to 1.");
        }

        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize < 1)
        {
            throw new ValidationException("pageSize must be greater than zero.");
        }

        if (pageSize > MaxPageSize)
        {
            throw new ValidationException($"pageSize must not exceed {MaxPageSize}.");
        }

        var filter = new OrderListFilter(query.CustomerId, query.Status, query.FromUtc, query.ToUtc, page, pageSize);

        // O repositório já projeta direto para OrderSummaryDto (sem itens), nada a mapear aqui.
        return await _orderRepository.ListAsync(filter, cancellationToken);
    }
}
