using NSubstitute;
using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Application.Orders.ConfirmOrder;
using OrderService.Domain.Common;
using OrderService.Domain.Orders;

namespace OrderService.Application.Tests.Orders.ConfirmOrder;

public class ConfirmOrderHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private ConfirmOrderHandler CreateHandler() => new(_orderRepository, _unitOfWork);

    [Fact]
    public async Task HandleAsync_WithPlacedOrder_ConfirmsAndPersists()
    {
        var order = CreatePlacedOrder();
        _orderRepository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().HandleAsync(new ConfirmOrderCommand(order.Id));

        Assert.Equal(OrderStatus.Confirmed, result.Status);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CalledTwice_IsIdempotentWithSameResult()
    {
        var order = CreatePlacedOrder();
        _orderRepository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var first = await CreateHandler().HandleAsync(new ConfirmOrderCommand(order.Id));
        var second = await CreateHandler().HandleAsync(new ConfirmOrderCommand(order.Id));

        Assert.Equal(OrderStatus.Confirmed, first.Status);
        Assert.Equal(OrderStatus.Confirmed, second.Status);
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithNonExistingOrder_ThrowsOrderNotFoundException()
    {
        var orderId = Guid.NewGuid();
        _orderRepository.GetByIdAsync(orderId, Arg.Any<CancellationToken>()).Returns((Order?)null);

        var exception = await Assert.ThrowsAsync<OrderNotFoundException>(
            () => CreateHandler().HandleAsync(new ConfirmOrderCommand(orderId)));

        Assert.Equal(orderId, exception.OrderId);
    }

    [Fact]
    public async Task HandleAsync_WithCanceledOrder_ThrowsInvalidOrderStateTransitionException()
    {
        var order = CreatePlacedOrder();
        order.Cancel();
        _orderRepository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        await Assert.ThrowsAsync<InvalidOrderStateTransitionException>(
            () => CreateHandler().HandleAsync(new ConfirmOrderCommand(order.Id)));
    }

    private static Order CreatePlacedOrder()
    {
        var items = new[] { new OrderItem(Guid.NewGuid(), new Money(10.00m, Currency.BRL), 1) };
        return Order.Place(Guid.NewGuid(), Currency.BRL, items, DateTime.UtcNow);
    }
}
