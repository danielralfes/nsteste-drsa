namespace OrderService.Application.Orders.CancelOrder;

public sealed record CancelOrderCommand(Guid OrderId);
