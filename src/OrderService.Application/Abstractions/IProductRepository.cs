using OrderService.Domain.Products;

namespace OrderService.Application.Abstractions;

public interface IProductRepository
{
    /// <summary>Retorna <see langword="null"/> se o produto não existir.</summary>
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca vários produtos de uma vez, pra evitar N+1 no <c>CreateOrderHandler</c>.
    /// IDs sem produto correspondente simplesmente não aparecem no resultado.
    /// </summary>
    Task<IReadOnlyCollection<Product>> GetByIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default);
}
