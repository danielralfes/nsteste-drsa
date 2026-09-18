using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain.Orders;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeamento Fluent API de <see cref="Order"/>.
/// </summary>
/// <remarks>
/// <see cref="Order.Total"/> é calculado em memória a partir de
/// <see cref="Order.Items"/> — o Domain não tem campo próprio pra isso, então
/// não dá pra mapear direto como coluna gravável. Por isso vira uma shadow
/// property, sincronizada a partir de <c>order.Total.Amount</c> em
/// <see cref="OrderServiceDbContext.SaveChangesAsync"/> antes de gravar (ver
/// esse método pro mecanismo de sincronização, e decisions.md seção 8 pro
/// porquê de persistir em vez de recomputar a cada leitura).
/// </remarks>
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);

        // Guid v7 é gerado no construtor do Domain, nunca pelo banco (decisions.md seção 11).
        builder.Property(o => o.Id)
            .ValueGeneratedNever();

        builder.Property(o => o.CustomerId)
            .IsRequired();

        // Enum vira string no banco — mais legível e não quebra se a ordem do enum mudar.
        builder.Property(o => o.Currency)
            .HasConversion<string>()
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        // O nome da shadow property é diferente do nome da propriedade CLR
        // de propósito — EF não deixa criar uma shadow property com o mesmo
        // nome de um membro CLR existente, mesmo que esse membro esteja ignorado.
        builder.Property<decimal>(OrderPersistenceConstants.TotalAmountShadowPropertyName)
            .HasColumnName("total")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        // A propriedade computada não é mapeada diretamente; só a shadow
        // property acima vira coluna de fato.
        builder.Ignore(o => o.Total);

        // Order.Items não tem setter público, é backed pelo campo privado
        // `_items` — EF acessa o campo direto.
        builder.Navigation(o => o.Items)
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey("OrderId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // Índice composto cobre os três filtros de GET /orders (customerId,
        // status, intervalo de CreatedAt) — decisions.md seção 12. CustomerId
        // vem primeiro por ser o filtro mais seletivo na prática, e casa com
        // o uso mais comum: filtrar por cliente, opcionalmente por status,
        // ordenado/filtrado por data.
        builder.HasIndex(o => new { o.CustomerId, o.Status, o.CreatedAt })
            .HasDatabaseName("IX_orders_CustomerId_Status_CreatedAt");
    }
}
