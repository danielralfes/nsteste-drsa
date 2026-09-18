using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain.Products;

namespace OrderService.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);

        // Guid v7 é gerado no construtor do Domain, nunca pelo banco (decisions.md seção 11).
        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.Name)
            .HasMaxLength(200)
            .IsRequired();

        // Mesma decisão de OrderItemConfiguration: Money é value object,
        // mapeado como Complex Type do EF Core 8+.
        builder.ComplexProperty(p => p.UnitPrice, unitPrice =>
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
