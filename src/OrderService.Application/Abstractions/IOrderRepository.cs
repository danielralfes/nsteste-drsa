using OrderService.Application.Common;
using OrderService.Application.Orders;
using OrderService.Domain.Orders;

namespace OrderService.Application.Abstractions;

/// <summary>Repositório do agregado <see cref="Order"/>.</summary>
/// <remarks>
/// <see cref="ListAsync"/> recebe um <see cref="OrderListFilter"/> e já devolve o
/// <see cref="PagedResult{T}"/> pronto, paginado e contado. Preferi isso a expor
/// <c>IQueryable&lt;Order&gt;</c> (vazaria o EF Core pra fora da Infrastructure) ou a
/// separar em <c>CountAsync</c>/<c>QueryAsync</c> (duas idas ao banco pra algo que o
/// Postgres resolve numa query só, com window function). Assim a Infrastructure fica
/// livre pra otimizar a query de verdade por trás de uma assinatura estável.
///
/// Repare que <see cref="ListAsync"/> retorna <see cref="OrderSummaryDto"/>, não
/// <see cref="Order"/> — a listagem projeta direto no banco pro formato de resumo do
/// <c>GET /orders</c>, sem materializar o agregado inteiro (nem os itens) por página.
/// É uma exceção deliberada ao resto da interface, que trabalha com a entidade de
/// domínio; faz sentido porque <see cref="ListAsync"/> é leitura pura pra exibição de
/// lista, nunca base de mutação. Já <see cref="GetByIdAsync"/> precisa retornar a
/// entidade completa e rastreada, porque dali pode sair um Confirm/Cancel.
/// </remarks>
public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    /// <summary>Retorna <see langword="null"/> se o pedido não existir.</summary>
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<OrderSummaryDto>> ListAsync(OrderListFilter filter, CancellationToken cancellationToken = default);
}
