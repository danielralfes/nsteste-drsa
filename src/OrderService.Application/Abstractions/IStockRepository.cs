namespace OrderService.Application.Abstractions;

/// <summary>
/// Abstração da reserva/liberação atômica de estoque. A atomicidade em si (UPDATE
/// condicional no banco) fica por conta da implementação; aqui é só o contrato
/// consumido pela Application.
/// </summary>
public interface IStockRepository
{
    /// <summary>Tenta mover <paramref name="quantity"/> unidades do produto de disponível para reservado.</summary>
    /// <remarks>
    /// A implementação real faz um <c>UPDATE ... WHERE available_quantity &gt;= @qty</c> e
    /// usa o <c>rowsAffected</c> pra saber se deu certo. Retorna
    /// <see cref="StockReservationResult"/> em vez de um <c>bool</c> puro porque, quando falha,
    /// precisamos saber quanto tinha disponível pra preencher
    /// <see cref="OrderService.Domain.Products.InsufficientStockException"/> sem fazer uma
    /// consulta extra — no caminho de sucesso essa informação nem é necessária.
    /// </remarks>
    Task<StockReservationResult> TryReserveAsync(Guid productId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Devolve <paramref name="quantity"/> unidades do produto de reservado para disponível. Usado no cancelamento.</summary>
    Task ReleaseAsync(Guid productId, int quantity, CancellationToken cancellationToken = default);
}

/// <summary>Resultado de uma tentativa de reserva de estoque.</summary>
public readonly record struct StockReservationResult(bool Success, int AvailableQuantity)
{
    public static StockReservationResult Reserved() => new(true, default);

    public static StockReservationResult InsufficientStock(int availableQuantity) => new(false, availableQuantity);
}
