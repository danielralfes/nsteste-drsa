using NSubstitute;
using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Application.Orders;
using OrderService.Application.Orders.ListOrders;
using OrderService.Domain.Common;
using OrderService.Domain.Orders;

namespace OrderService.Application.Tests.Orders.ListOrders;

public class ListOrdersHandlerTests
{
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();

    private ListOrdersHandler CreateHandler() => new(_orderRepository);

    [Fact]
    public async Task HandleAsync_WithoutPagingParameters_UsesDefaultsAndReturnsRepositoryTotals()
    {
        _orderRepository
            .ListAsync(Arg.Any<OrderListFilter>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<OrderSummaryDto>(Array.Empty<OrderSummaryDto>(), Page: 1, PageSize: ListOrdersHandler.DefaultPageSize, TotalCount: 45));

        var result = await CreateHandler().HandleAsync(new ListOrdersQuery(null, null, null, null, null, null));

        Assert.Equal(1, result.Page);
        Assert.Equal(ListOrdersHandler.DefaultPageSize, result.PageSize);
        Assert.Equal(45, result.TotalCount);
        Assert.Equal(3, result.TotalPages);

        await _orderRepository.Received(1).ListAsync(
            Arg.Is<OrderListFilter>(f => f.Page == 1 && f.PageSize == ListOrdersHandler.DefaultPageSize),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithPageSizeAboveMaximum_ThrowsValidationException()
    {
        var query = new ListOrdersQuery(null, null, null, null, 1, ListOrdersHandler.MaxPageSize + 1);

        await Assert.ThrowsAsync<ValidationException>(() => CreateHandler().HandleAsync(query));

        await _orderRepository.DidNotReceive().ListAsync(Arg.Any<OrderListFilter>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task HandleAsync_WithNonPositivePageSize_ThrowsValidationException(int pageSize)
    {
        var query = new ListOrdersQuery(null, null, null, null, 1, pageSize);

        await Assert.ThrowsAsync<ValidationException>(() => CreateHandler().HandleAsync(query));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task HandleAsync_WithNonPositivePage_ThrowsValidationExceptionAndDoesNotQueryRepository(int page)
    {
        var query = new ListOrdersQuery(null, null, null, null, page, null);

        await Assert.ThrowsAsync<ValidationException>(() => CreateHandler().HandleAsync(query));

        await _orderRepository.DidNotReceive().ListAsync(Arg.Any<OrderListFilter>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithFilters_PassesThemToRepository()
    {
        var customerId = Guid.NewGuid();
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 31, 23, 59, 59, DateTimeKind.Utc);

        _orderRepository
            .ListAsync(Arg.Any<OrderListFilter>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<OrderSummaryDto>(Array.Empty<OrderSummaryDto>(), Page: 2, PageSize: 10, TotalCount: 0));

        var query = new ListOrdersQuery(customerId, OrderStatus.Confirmed, from, to, 2, 10);

        await CreateHandler().HandleAsync(query);

        await _orderRepository.Received(1).ListAsync(
            Arg.Is<OrderListFilter>(f =>
                f.CustomerId == customerId &&
                f.Status == OrderStatus.Confirmed &&
                f.FromUtc == from &&
                f.ToUtc == to &&
                f.Page == 2 &&
                f.PageSize == 10),
            Arg.Any<CancellationToken>());
    }
}
