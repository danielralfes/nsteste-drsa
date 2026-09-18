using Microsoft.EntityFrameworkCore;
using OrderService.Application.Abstractions;
using OrderService.Application.Common;
using OrderService.Application.Orders;
using OrderService.Domain.Orders;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Persistence.Repositories;

public sealed class OrderRepository : IOrderRepository
{
    private readonly OrderServiceDbContext _context;

    public OrderRepository(OrderServiceDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        await _context.Orders.AddAsync(order, cancellationToken);
    }

    /// <remarks>
    /// Deliberadamente sem <c>AsNoTracking()</c>: este método é usado tanto
    /// pela leitura pura (<c>GetOrderByIdHandler</c>) quanto pelos casos de
    /// uso de mutação (<c>ConfirmOrderHandler</c>, <c>CancelOrderHandler</c>),
    /// que chamam <c>order.Confirm()</c>/<c>Cancel()</c> e esperam que o
    /// change tracking do EF grave a mudança quando <c>IUnitOfWork.SaveChangesAsync()</c>
    /// for chamado. Com <c>AsNoTracking()</c> isso viraria um no-op
    /// silencioso — já aconteceu, não é hipotético. Desvio proposital da
    /// sugestão do plano original desta fatia, documentado no resumo final
    /// da entrega.
    /// </remarks>
    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    /// <remarks>
    /// Projeta direto pra <see cref="OrderSummaryDto"/> no banco, sem
    /// <c>Include(Items)</c> — a listagem paginada não expõe os itens de cada
    /// pedido (decisions.md seções 12 e 25). Isso evita carregar a coleção de
    /// <c>OrderItem</c> inteira só pra descartar depois do mapeamento, e
    /// também tira a necessidade de <c>AsSplitQuery()</c> aqui, já que não
    /// tem mais JOIN com tabela filha. O total já vem pronto da shadow
    /// property <c>TotalAmount</c> persistida via <see cref="EF.Property{TProperty}(object, string)"/>,
    /// sem recomputar a partir dos itens. No fim são só 2 queries por
    /// chamada (COUNT + SELECT paginado), nenhuma proporcional ao número de
    /// pedidos da página.
    /// </remarks>
    public async Task<PagedResult<OrderSummaryDto>> ListAsync(OrderListFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _context.Orders.AsNoTracking().AsQueryable();

        if (filter.CustomerId is { } customerId)
        {
            query = query.Where(o => o.CustomerId == customerId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(o => o.Status == status);
        }

        if (filter.FromUtc is { } fromUtc)
        {
            query = query.Where(o => o.CreatedAt >= fromUtc);
        }

        if (filter.ToUtc is { } toUtc)
        {
            query = query.Where(o => o.CreatedAt <= toUtc);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(o => new OrderSummaryDto(
                o.Id,
                o.CustomerId,
                o.Currency,
                o.Status,
                EF.Property<decimal>(o, OrderPersistenceConstants.TotalAmountShadowPropertyName),
                o.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderSummaryDto>(items, filter.Page, filter.PageSize, totalCount);
    }
}
