using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Domain.Orders;
using OrderService.Domain.Products;

namespace OrderService.Application.Orders.CreateOrder;

/// <summary>Caso de uso <c>POST /orders</c>.</summary>
public sealed class CreateOrderHandler
{
    private readonly IProductRepository _productRepository;
    private readonly IStockRepository _stockRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreateOrderHandler(
        IProductRepository productRepository,
        IStockRepository stockRepository,
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(productRepository);
        ArgumentNullException.ThrowIfNull(stockRepository);
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(clock);

        _productRepository = productRepository;
        _stockRepository = stockRepository;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<OrderDto> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Carrega todos os produtos numa chamada só (evita N+1) e monta as linhas do
        // pedido com preço/currency vindos do catálogo. Order.Place compara essa
        // currency com a do pedido; OrderItem já valida quantidade > 0.
        var productIds = command.Items.Select(i => i.ProductId).Distinct().ToArray();
        var products = await _productRepository.GetByIdsAsync(productIds, cancellationToken);
        var productsById = products.ToDictionary(p => p.Id);

        var items = new List<OrderItem>(command.Items.Count);
        foreach (var itemCommand in command.Items)
        {
            if (!productsById.TryGetValue(itemCommand.ProductId, out var product))
            {
                throw new ProductNotFoundException(itemCommand.ProductId);
            }

            items.Add(new OrderItem(product.Id, product.UnitPrice, itemCommand.Quantity));
        }

        // Order.Place já valida lista vazia, currency divergente entre pedido/itens e
        // ProductId duplicado — nada disso é reimplementado aqui.
        var order = Order.Place(command.CustomerId, command.Currency, items, _clock.UtcNow);

        // Reserva de estoque de todos os itens + insert do pedido na mesma transação:
        // se algum item não tiver estoque suficiente, a exceção aborta o delegate
        // inteiro antes do AddAsync, e as reservas já aplicadas são revertidas.
        await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                foreach (var item in order.Items)
                {
                    var reservation = await _stockRepository.TryReserveAsync(item.ProductId, item.Quantity, ct);
                    if (!reservation.Success)
                    {
                        throw new InsufficientStockException(item.ProductId, item.Quantity, reservation.AvailableQuantity);
                    }
                }

                await _orderRepository.AddAsync(order, ct);
                await _unitOfWork.SaveChangesAsync(ct);
            },
            cancellationToken);

        return OrderDto.FromDomain(order);
    }
}
