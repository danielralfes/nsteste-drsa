using OrderService.Domain.Common;

namespace OrderService.Domain.Orders;

/// <summary>
/// Lançada quando uma transição de estado inválida é tentada em
/// <see cref="Order"/> (ex.: confirmar um pedido cancelado).
/// </summary>
public sealed class InvalidOrderStateTransitionException : DomainException
{
    public InvalidOrderStateTransitionException(OrderStatus currentStatus, OrderStatus attemptedStatus)
        : base($"Cannot transition order from '{currentStatus}' to '{attemptedStatus}'.")
    {
        CurrentStatus = currentStatus;
        AttemptedStatus = attemptedStatus;
    }

    public OrderStatus CurrentStatus { get; }

    public OrderStatus AttemptedStatus { get; }
}
