namespace OrderService.Infrastructure.Persistence;

/// <summary>
/// Constantes de mapeamento usadas em mais de um lugar da persistência de
/// <c>Order</c>, pra não repetir string mágica entre <see cref="OrderServiceDbContext"/>,
/// <see cref="Configurations.OrderConfiguration"/> e <see cref="Repositories.OrderRepository"/>
/// (decisions.md seção 21.3).
/// </summary>
internal static class OrderPersistenceConstants
{
    /// <summary>
    /// Nome da shadow property que guarda o total persistido de
    /// <see cref="Domain.Orders.Order"/> (decisions.md seção 8). É diferente
    /// do nome da propriedade CLR <c>Total</c> de propósito — EF não deixa
    /// usar o mesmo nome de um membro CLR existente, mesmo ignorado.
    /// </summary>
    internal const string TotalAmountShadowPropertyName = "TotalAmount";
}
