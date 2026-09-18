using OrderService.Domain.Common;

namespace OrderService.Domain.Products;

/// <summary>
/// Estoque de um produto, com dois contadores: <see cref="AvailableQuantity"/>
/// e <see cref="ReservedQuantity"/>. Fluxo completo de reserva/confirmação/
/// cancelamento em decisions.md seção 1.
/// </summary>
/// <remarks>
/// A atomicidade sob concorrência de verdade vem, na Fatia 3, de um `UPDATE`
/// condicional no banco (`WHERE available_quantity >= @qty`). Os métodos
/// abaixo só espelham essa regra em memória, pra dar pra testar isoladamente
/// no domínio — não substituem a checagem no banco.
/// </remarks>
public sealed class Stock
{
    /// <exception cref="InvalidQuantityException">
    /// <paramref name="availableQuantity"/> ou <paramref name="reservedQuantity"/> é negativa.
    /// </exception>
    public Stock(Guid productId, int availableQuantity, int reservedQuantity = 0)
    {
        if (availableQuantity < 0)
        {
            throw new InvalidQuantityException(availableQuantity, "greater than or equal to zero");
        }

        if (reservedQuantity < 0)
        {
            throw new InvalidQuantityException(reservedQuantity, "greater than or equal to zero");
        }

        ProductId = productId;
        AvailableQuantity = availableQuantity;
        ReservedQuantity = reservedQuantity;
    }

    public Guid ProductId { get; }

    public int AvailableQuantity { get; private set; }

    public int ReservedQuantity { get; private set; }

    /// <summary>Tenta mover <paramref name="quantity"/> unidades de <see cref="AvailableQuantity"/> para <see cref="ReservedQuantity"/>.</summary>
    /// <returns>
    /// <c>true</c> se tinha estoque suficiente e a reserva foi feita;
    /// <c>false</c> se não tinha — estoque insuficiente é resultado normal de
    /// negócio, não exceção, então cabe à Application layer decidir como
    /// tratar (ex.: lançar <see cref="InsufficientStockException"/>, espelhando
    /// o `rowsAffected == 0` do `UPDATE` condicional no banco).
    /// </returns>
    /// <exception cref="InvalidQuantityException">
    /// <paramref name="quantity"/> é zero ou negativa. Isso é erro de uso, não
    /// resultado de negócio esperado, por isso lança em vez de retornar <c>false</c>.
    /// </exception>
    public bool TryReserve(int quantity)
    {
        if (quantity <= 0)
        {
            throw new InvalidQuantityException(quantity);
        }

        if (AvailableQuantity < quantity)
        {
            return false;
        }

        AvailableQuantity -= quantity;
        ReservedQuantity += quantity;
        return true;
    }

    /// <summary>
    /// Devolve <paramref name="quantity"/> unidades de <see cref="ReservedQuantity"/>
    /// para <see cref="AvailableQuantity"/>. Usado no cancelamento de pedido.
    /// </summary>
    /// <exception cref="InvalidQuantityException">
    /// <paramref name="quantity"/> é zero ou negativa.
    /// </exception>
    /// <exception cref="StockReleaseExceedsReservedException">
    /// <paramref name="quantity"/> é maior do que <see cref="ReservedQuantity"/>.
    /// </exception>
    public void Release(int quantity)
    {
        if (quantity <= 0)
        {
            throw new InvalidQuantityException(quantity);
        }

        if (quantity > ReservedQuantity)
        {
            throw new StockReleaseExceedsReservedException(ProductId, quantity, ReservedQuantity);
        }

        ReservedQuantity -= quantity;
        AvailableQuantity += quantity;
    }
}
