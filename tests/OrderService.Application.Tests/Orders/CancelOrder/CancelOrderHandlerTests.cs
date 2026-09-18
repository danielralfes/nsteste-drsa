using NSubstitute;
using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Application.Orders.CancelOrder;
using OrderService.Application.Tests.TestSupport;
using OrderService.Domain.Common;
using OrderService.Domain.Orders;

namespace OrderService.Application.Tests.Orders.CancelOrder;

public class CancelOrderHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IStockRepository _stockRepository = Substitute.For<IStockRepository>();
    private readonly IUnitOfWork _unitOfWork = UnitOfWorkSubstitute.Create();

    private CancelOrderHandler CreateHandler() => new(_orderRepository, _stockRepository, _unitOfWork);

    [Fact]
    public async Task HandleAsync_WithPlacedOrder_CancelsAndReleasesStockForEachItem()
    {
        var productId = Guid.NewGuid();
        var order = CreatePlacedOrder(productId, quantity: 3);
        _orderRepository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().HandleAsync(new CancelOrderCommand(order.Id));

        Assert.Equal(OrderStatus.Canceled, result.Status);
        await _stockRepository.Received(1).ReleaseAsync(productId, 3, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithConfirmedOrder_CancelsAndReleasesStock()
    {
        var productId = Guid.NewGuid();
        var order = CreatePlacedOrder(productId, quantity: 2);
        order.Confirm();
        _orderRepository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().HandleAsync(new CancelOrderCommand(order.Id));

        Assert.Equal(OrderStatus.Canceled, result.Status);
        await _stockRepository.Received(1).ReleaseAsync(productId, 2, Arg.Any<CancellationToken>());
    }

    // Esse aqui é o mais importante: cancelar um pedido já cancelado NÃO pode
    // liberar estoque de novo.
    [Fact]
    public async Task HandleAsync_CalledTwice_DoesNotReleaseStockOnSecondCall()
    {
        var productId = Guid.NewGuid();
        var order = CreatePlacedOrder(productId, quantity: 3);
        _orderRepository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var handler = CreateHandler();

        var first = await handler.HandleAsync(new CancelOrderCommand(order.Id));
        _stockRepository.ClearReceivedCalls();
        _unitOfWork.ClearReceivedCalls();

        var second = await handler.HandleAsync(new CancelOrderCommand(order.Id));

        Assert.Equal(OrderStatus.Canceled, first.Status);
        Assert.Equal(OrderStatus.Canceled, second.Status);
        await _stockRepository.DidNotReceive().ReleaseAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithNonExistingOrder_ThrowsOrderNotFoundException()
    {
        var orderId = Guid.NewGuid();
        _orderRepository.GetByIdAsync(orderId, Arg.Any<CancellationToken>()).Returns((Order?)null);

        var exception = await Assert.ThrowsAsync<OrderNotFoundException>(
            () => CreateHandler().HandleAsync(new CancelOrderCommand(orderId)));

        Assert.Equal(orderId, exception.OrderId);
    }

    private static Order CreatePlacedOrder(Guid productId, int quantity)
    {
        var items = new[] { new OrderItem(productId, new Money(10.00m, Currency.BRL), quantity) };
        return Order.Place(Guid.NewGuid(), Currency.BRL, items, DateTime.UtcNow);
    }
}
