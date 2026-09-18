using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Orders;
using OrderService.Domain.Products;

namespace OrderService.Infrastructure.Persistence;

public sealed class OrderServiceDbContext : DbContext
{
    public OrderServiceDbContext(DbContextOptions<OrderServiceDbContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Stock> Stocks => Set<Stock>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderServiceDbContext).Assembly);
    }

    /// <summary>
    /// Sincroniza a shadow property de total (ver <see cref="Configurations.OrderConfiguration"/>)
    /// a partir de <see cref="Order.Total"/> antes de gravar — é o único
    /// lugar onde isso acontece, já que o Domain não guarda o total em campo
    /// próprio, só calcula em memória (decisions.md seção 8).
    /// <para>
    /// Importante: todo write de <see cref="Order"/> precisa passar por aqui.
    /// Se algum código gravar a tabela <c>orders</c> por fora — <c>ExecuteUpdateAsync</c>,
    /// SQL raw, outro <see cref="DbContext"/> na mesma tabela — o total
    /// persistido dessincroniza silenciosamente, e nada hoje detecta isso
    /// (decisions.md seção 21.3).
    /// </para>
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SyncOrderTotals();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        SyncOrderTotals();
        return base.SaveChanges();
    }

    private void SyncOrderTotals()
    {
        foreach (var entry in ChangeTracker.Entries<Order>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property(OrderPersistenceConstants.TotalAmountShadowPropertyName).CurrentValue = entry.Entity.Total.Amount;
            }
        }
    }
}
