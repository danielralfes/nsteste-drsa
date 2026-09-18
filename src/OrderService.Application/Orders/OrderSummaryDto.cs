using OrderService.Domain.Common;
using OrderService.Domain.Orders;

namespace OrderService.Application.Orders;

/// <summary>Projeção resumida de <see cref="Order"/> usada só por <c>GET /orders</c> (listagem paginada).</summary>
/// <remarks>
/// Sem a coleção de itens de propósito: a listagem não precisa do detalhe de linha pra
/// renderizar uma tabela de resultados, e carregar <c>OrderItem</c> por página só pra
/// descartar depois custa I/O e memória à toa. O detalhe completo, com itens, continua em
/// <c>GET /orders/{id}</c>, que retorna <see cref="OrderDto"/>.
/// </remarks>
public sealed record OrderSummaryDto(
    Guid Id,
    Guid CustomerId,
    Currency Currency,
    OrderStatus Status,
    decimal Total,
    DateTime CreatedAt);
