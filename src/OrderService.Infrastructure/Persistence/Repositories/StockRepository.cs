using Microsoft.EntityFrameworkCore;
using OrderService.Application.Abstractions;
using OrderService.Domain.Products;

namespace OrderService.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementação real do <c>UPDATE</c> condicional atômico descrito em
/// decisions.md seções 1 e 5.
/// </summary>
/// <remarks>
/// Usa <c>ExecuteUpdateAsync</c> em vez de SQL raw — compila pra um único
/// <c>UPDATE ... WHERE</c>, sem precisar carregar a entidade (sem SELECT +
/// tracking + SaveChanges). A condição de disponibilidade
/// (<c>AvailableQuantity >= quantity</c>) fica na cláusula <c>WHERE</c> do
/// próprio LINQ, então o <c>rowsAffected</c> já diz se a reserva foi
/// aplicada ou não.
/// </remarks>
public sealed class StockRepository : IStockRepository
{
    private readonly OrderServiceDbContext _context;

    public StockRepository(OrderServiceDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    public async Task<StockReservationResult> TryReserveAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
    {
        var rowsAffected = await _context.Stocks
            .Where(s => s.ProductId == productId && s.AvailableQuantity >= quantity)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.AvailableQuantity, s => s.AvailableQuantity - quantity)
                    .SetProperty(s => s.ReservedQuantity, s => s.ReservedQuantity + quantity),
                cancellationToken);

        if (rowsAffected > 0)
        {
            return StockReservationResult.Reserved();
        }

        // Só roda no caminho de falha: busca o AvailableQuantity atual pra
        // preencher InsufficientStockException.AvailableQuantity. Não tem
        // custo nenhum quando a reserva dá certo (decisions.md seção 1).
        var currentAvailable = await _context.Stocks
            .AsNoTracking()
            .Where(s => s.ProductId == productId)
            .Select(s => (int?)s.AvailableQuantity)
            .FirstOrDefaultAsync(cancellationToken);

        return StockReservationResult.InsufficientStock(currentAvailable ?? 0);
    }

    public async Task ReleaseAsync(Guid productId, int quantity, CancellationToken cancellationToken = default)
    {
        // Liberação não é condicional de negócio, deveria sempre "funcionar"
        // se o pedido tinha aquela quantidade reservada. Mas clampamos
        // ReservedQuantity em zero por segurança, caso role algum estado
        // inconsistente (decisions.md seção 17).
        await _context.Stocks
            .Where(s => s.ProductId == productId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.AvailableQuantity, s => s.AvailableQuantity + quantity)
                    .SetProperty(s => s.ReservedQuantity, s => s.ReservedQuantity - quantity < 0 ? 0 : s.ReservedQuantity - quantity),
                cancellationToken);
    }
}
