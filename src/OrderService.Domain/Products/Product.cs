using OrderService.Domain.Common;

namespace OrderService.Domain.Products;

/// <summary>
/// Item de catálogo. O catálogo é somente-leitura nesta v1 (seed, decisions.md
/// seção 6), então <see cref="Product"/> não tem métodos de mutação além da criação.
/// </summary>
public sealed class Product
{
    /// <summary>
    /// Construtor vazio pro EF Core (Fatia 3) — mesmo motivo de
    /// <see cref="Orders.OrderItem"/>: EF não vincula construtor a
    /// propriedades complexas (<c>unitPrice</c>).
    /// </summary>
    private Product()
    {
        Name = string.Empty;
    }

    /// <exception cref="InvalidProductNameException">
    /// <paramref name="name"/> é vazio ou só espaço em branco.
    /// </exception>
    /// <exception cref="InvalidPriceException">
    /// <paramref name="unitPrice"/> tem <see cref="Money.Amount"/> negativo (decisions.md seção 17).
    /// </exception>
    public Product(string name, Money unitPrice)
        : this(Guid.CreateVersion7(), name, unitPrice)
    {
    }

    /// <summary>
    /// Construtor com <paramref name="id"/> explícito, visível só pra
    /// <c>OrderService.Infrastructure</c> (via <c>InternalsVisibleTo</c>).
    /// Usado pelo seeder idempotente (decisions.md seção 6), que precisa de
    /// Guids fixos pra checar "já existe" a cada restart. Fora do
    /// Domain/Infrastructure, o único jeito de criar um <see cref="Product"/>
    /// continua sendo o construtor público.
    /// </summary>
    internal Product(Guid id, string name, Money unitPrice)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidProductNameException();
        }

        if (unitPrice.Amount < 0)
        {
            throw new InvalidPriceException(unitPrice.Amount);
        }

        Id = id;
        Name = name;
        UnitPrice = unitPrice;
    }

    public Guid Id { get; }

    public string Name { get; }

    public Money UnitPrice { get; }
}
