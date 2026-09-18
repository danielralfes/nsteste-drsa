using OrderService.Application.Abstractions;
using OrderService.Application.Common;

namespace OrderService.Application.Orders.GetOrderById;

/// <summary>Caso de uso <c>GET /orders/{id}</c>.</summary>
public sealed class GetOrderByIdHandler
{
    private readonly IOrderRepository _orderRepository;

    public GetOrderByIdHandler(IOrderRepository orderRepository)
    {
        ArgumentNullException.ThrowIfNull(orderRepository);

        _orderRepository = orderRepository;
    }

    public async Task<OrderDto> HandleAsync(GetOrderByIdQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var order = await _orderRepository.GetByIdAsync(query.OrderId, cancellationToken)
            ?? throw new OrderNotFoundException(query.OrderId);

        return OrderDto.FromDomain(order);
    }
}
