using NSubstitute;
using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Application.Orders.GetOrderById;
using OrderService.Domain.Common;
using OrderService.Domain.Orders;

namespace OrderService.Application.Tests.Orders.GetOrderById;

public class GetOrderByIdHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();

    private GetOrderByIdHandler CreateHandler() => new(_orderRepository);

    [Fact]
    public async Task HandleAsync_WithExistingOrder_ReturnsMatchingDto()
    {
        var items = new[] { new OrderItem(Guid.NewGuid(), new Money(10.00m, Currency.BRL), 2) };
        var order = Order.Place(Guid.NewGuid(), Currency.BRL, items, DateTime.UtcNow);
        _orderRepository.GetByIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var result = await CreateHandler().HandleAsync(new GetOrderByIdQuery(order.Id));

        Assert.Equal(order.Id, result.Id);
        Assert.Equal(order.CustomerId, result.CustomerId);
        Assert.Equal(order.Total.Amount, result.Total);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task HandleAsync_WithNonExistingOrder_ThrowsOrderNotFoundException()
    {
        var orderId = Guid.NewGuid();
        _orderRepository.GetByIdAsync(orderId, Arg.Any<CancellationToken>()).Returns((Order?)null);

        var exception = await Assert.ThrowsAsync<OrderNotFoundException>(
            () => CreateHandler().HandleAsync(new GetOrderByIdQuery(orderId)));

        Assert.Equal(orderId, exception.OrderId);
    }
}
