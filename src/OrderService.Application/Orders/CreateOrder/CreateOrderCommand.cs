using OrderService.Domain.Common;

namespace OrderService.Application.Orders.CreateOrder;

public sealed record CreateOrderCommand(Guid CustomerId, Currency Currency, IReadOnlyCollection<CreateOrderItemCommand> Items);

public sealed record CreateOrderItemCommand(Guid ProductId, int Quantity);
