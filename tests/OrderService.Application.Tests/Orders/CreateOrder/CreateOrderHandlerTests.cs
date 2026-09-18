using NSubstitute;
using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Application.Orders.CreateOrder;
using OrderService.Application.Tests.TestSupport;
using OrderService.Domain.Common;
using OrderService.Domain.Orders;
using OrderService.Domain.Products;

namespace OrderService.Application.Tests.Orders.CreateOrder;

public class CreateOrderHandlerTests
{
    private readonly IProductRepository _productRepository = Substitute.For<IProductRepository>();
    private readonly IStockRepository _stockRepository = Substitute.For<IStockRepository>();
    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IUnitOfWork _unitOfWork = UnitOfWorkSubstitute.Create();
    private readonly IClock _clock = Substitute.For<IClock>();

    private CreateOrderHandler CreateHandler() =>
        new(_productRepository, _stockRepository, _orderRepository, _unitOfWork, _clock);

    [Fact]
    public async Task HandleAsync_WithValidItems_CalculatesTotalAndPersistsOrder()
    {
        var productA = new Product("Widget", new Money(10.00m, Currency.BRL));
        var productB = new Product("Gadget", new Money(5.50m, Currency.BRL));

        _productRepository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { productA, productB });

        _stockRepository.TryReserveAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StockReservationResult.Reserved());

        var command = new CreateOrderCommand(
            Guid.NewGuid(),
            Currency.BRL,
            new[]
            {
                new CreateOrderItemCommand(productA.Id, 2),
                new CreateOrderItemCommand(productB.Id, 3),
            });

        var result = await CreateHandler().HandleAsync(command);

        Assert.Equal(36.50m, result.Total);
        Assert.Equal(OrderStatus.Placed, result.Status);
        Assert.Equal(2, result.Items.Count);

        await _orderRepository.Received(1).AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _stockRepository.Received(1).TryReserveAsync(productA.Id, 2, Arg.Any<CancellationToken>());
        await _stockRepository.Received(1).TryReserveAsync(productB.Id, 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithNonExistingProduct_ThrowsProductNotFoundExceptionAndPersistsNothing()
    {
        var missingProductId = Guid.NewGuid();
        _productRepository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Product>());

        var command = new CreateOrderCommand(
            Guid.NewGuid(),
            Currency.BRL,
            new[] { new CreateOrderItemCommand(missingProductId, 1) });

        var exception = await Assert.ThrowsAsync<ProductNotFoundException>(() => CreateHandler().HandleAsync(command));

        Assert.Equal(missingProductId, exception.ProductId);
        await _orderRepository.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithInsufficientStock_ThrowsInsufficientStockExceptionAndPersistsNothing()
    {
        var product = new Product("Widget", new Money(10.00m, Currency.BRL));
        _productRepository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { product });

        _stockRepository.TryReserveAsync(product.Id, 5, Arg.Any<CancellationToken>())
            .Returns(StockReservationResult.InsufficientStock(2));

        var command = new CreateOrderCommand(
            Guid.NewGuid(),
            Currency.BRL,
            new[] { new CreateOrderItemCommand(product.Id, 5) });

        var exception = await Assert.ThrowsAsync<InsufficientStockException>(() => CreateHandler().HandleAsync(command));

        Assert.Equal(product.Id, exception.ProductId);
        Assert.Equal(5, exception.RequestedQuantity);
        Assert.Equal(2, exception.AvailableQuantity);

        await _orderRepository.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithSecondItemInsufficientStock_DoesNotPersistOrder()
    {
        var productA = new Product("Widget", new Money(10.00m, Currency.BRL));
        var productB = new Product("Gadget", new Money(5.00m, Currency.BRL));

        _productRepository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { productA, productB });

        _stockRepository.TryReserveAsync(productA.Id, 1, Arg.Any<CancellationToken>())
            .Returns(StockReservationResult.Reserved());
        _stockRepository.TryReserveAsync(productB.Id, 1, Arg.Any<CancellationToken>())
            .Returns(StockReservationResult.InsufficientStock(0));

        var command = new CreateOrderCommand(
            Guid.NewGuid(),
            Currency.BRL,
            new[]
            {
                new CreateOrderItemCommand(productA.Id, 1),
                new CreateOrderItemCommand(productB.Id, 1),
            });

        await Assert.ThrowsAsync<InsufficientStockException>(() => CreateHandler().HandleAsync(command));

        await _orderRepository.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithProductCurrencyDifferentFromOrderCurrency_ThrowsCurrencyMismatchException()
    {
        var product = new Product("Widget", new Money(10.00m, Currency.USD));
        _productRepository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { product });

        var command = new CreateOrderCommand(
            Guid.NewGuid(),
            Currency.BRL,
            new[] { new CreateOrderItemCommand(product.Id, 1) });

        await Assert.ThrowsAsync<CurrencyMismatchException>(() => CreateHandler().HandleAsync(command));

        await _orderRepository.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithDuplicateProductInItems_ThrowsDuplicateProductInOrderException()
    {
        var product = new Product("Widget", new Money(10.00m, Currency.BRL));
        _productRepository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { product });

        var command = new CreateOrderCommand(
            Guid.NewGuid(),
            Currency.BRL,
            new[]
            {
                new CreateOrderItemCommand(product.Id, 1),
                new CreateOrderItemCommand(product.Id, 2),
            });

        var exception = await Assert.ThrowsAsync<DuplicateProductInOrderException>(() => CreateHandler().HandleAsync(command));

        Assert.Equal(product.Id, exception.ProductId);
        await _orderRepository.DidNotReceive().AddAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithValidItems_SetsCreatedAtFromClock()
    {
        var fixedUtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _clock.UtcNow.Returns(fixedUtcNow);

        var product = new Product("Widget", new Money(10.00m, Currency.BRL));
        _productRepository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { product });

        _stockRepository.TryReserveAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(StockReservationResult.Reserved());

        var command = new CreateOrderCommand(
            Guid.NewGuid(),
            Currency.BRL,
            new[] { new CreateOrderItemCommand(product.Id, 1) });

        var result = await CreateHandler().HandleAsync(command);

        Assert.Equal(fixedUtcNow, result.CreatedAt);
    }
}
