using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderService.Domain.Products;

namespace OrderService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeamento Fluent API de <see cref="Stock"/>. <see cref="Stock.ProductId"/>
/// é a própria PK — 1:1 conceitual com <see cref="Product"/>, mas os dois
/// ficam como agregados/tabelas separados (já modelado assim no Domain). Não
/// tem FK declarada pra <c>products</c>: são agregados independentes, e é a
/// Application que garante que o produto existe antes de mexer no estoque.
/// </summary>
public sealed class StockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> builder)
    {
        builder.ToTable("stock");

        builder.HasKey(s => s.ProductId);

        // ProductId sempre vem explicitamente (é a PK/FK conceitual de
        // Product), nunca é gerado pelo banco nem pelo construtor — por isso
        // ValueGeneratedNever aqui também.
        builder.Property(s => s.ProductId)
            .ValueGeneratedNever();

        builder.Property(s => s.AvailableQuantity)
            .HasColumnName("available_quantity")
            .IsRequired();

        builder.Property(s => s.ReservedQuantity)
            .HasColumnName("reserved_quantity")
            .IsRequired();
    }
}
