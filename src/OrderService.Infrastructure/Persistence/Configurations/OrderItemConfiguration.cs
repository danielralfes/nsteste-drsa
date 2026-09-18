using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain.Orders;

namespace OrderService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeamento Fluent API de <see cref="OrderItem"/> — entidade filha de
/// <see cref="Order"/>, FK <c>OrderId</c> configurada em
/// <see cref="OrderConfiguration"/> via <c>HasMany</c>.
/// </summary>
public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .ValueGeneratedNever();

        builder.Property(i => i.ProductId)
            .IsRequired();

        builder.Property(i => i.Quantity)
            .IsRequired();

        // Money é um readonly record struct (value object), mapeado como
        // Complex Type do EF Core 8+ — não Owned Type/OwnsOne, que é pra
        // tipos de referência com identidade própria. Amount e Currency
        // ficam como colunas na própria tabela, sem tabela separada.
        builder.ComplexProperty(i => i.UnitPrice, unitPrice =>
        {
            unitPrice.Property(m => m.Amount)
                .HasColumnName("unit_price_amount")
                .HasColumnType("decimal(18,2)")
                .IsRequired();

            unitPrice.Property(m => m.Currency)
                .HasColumnName("unit_price_currency")
                .HasConversion<string>()
                .HasMaxLength(3)
                .IsRequired();
        });
    }
}
