using OrderService.Application.Abstractions;
using OrderService.Application.Common;

namespace OrderService.Application.Orders.CancelOrder;

/// <summary>
/// Caso de uso <c>POST /orders/{id}/cancel</c>. Só libera estoque quando esta chamada
/// de fato transicionou o pedido pra <c>Canceled</c> —
/// <see cref="Domain.Orders.Order.Cancel"/> retorna <c>false</c> se já estava
/// <c>Canceled</c>, evitando liberar a mesma reserva duas vezes numa chamada repetida.
/// </summary>
public sealed class CancelOrderHandler
{
    private readonly IOrderRepository _orderRepository;
    private readonly IStockRepository _stockRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CancelOrderHandler(IOrderRepository orderRepository, IStockRepository stockRepository, IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(orderRepository);
        ArgumentNullException.ThrowIfNull(stockRepository);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _orderRepository = orderRepository;
        _stockRepository = stockRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<OrderDto> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var order = await _orderRepository.GetByIdAsync(command.OrderId, cancellationToken)
            ?? throw new OrderNotFoundException(command.OrderId);

        var stateChanged = order.Cancel();

        if (stateChanged)
        {
            await _unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    foreach (var item in order.Items)
                    {
                        await _stockRepository.ReleaseAsync(item.ProductId, item.Quantity, ct);
                    }

                    await _unitOfWork.SaveChangesAsync(ct);
                },
                cancellationToken);
        }

        return OrderDto.FromDomain(order);
    }
}
