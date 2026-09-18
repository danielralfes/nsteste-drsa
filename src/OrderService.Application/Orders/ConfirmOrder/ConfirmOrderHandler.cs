using OrderService.Application.Abstractions;
using OrderService.Application.Common;

namespace OrderService.Application.Orders.ConfirmOrder;

/// <summary>
/// Caso de uso <c>POST /orders/{id}/confirm</c>. A idempotência é do domínio
/// (<see cref="OrderService.Domain.Orders.Order.Confirm"/> não faz nada se já
/// <c>Confirmed</c>). Por ora não decrementa <c>ReservedQuantity</c> no confirm.
/// </summary>
public sealed class ConfirmOrderHandler
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ConfirmOrderHandler(IOrderRepository orderRepository, IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<OrderDto> HandleAsync(ConfirmOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await _orderRepository.GetByIdAsync(command.OrderId, cancellationToken)
            ?? throw new OrderNotFoundException(command.OrderId);

        order.Confirm();

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return OrderDto.FromDomain(order);
    }
}
