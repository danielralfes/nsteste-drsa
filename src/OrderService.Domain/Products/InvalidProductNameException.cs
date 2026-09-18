using OrderService.Domain.Common;

namespace OrderService.Domain.Products;

/// <summary>Lançada quando <see cref="Product.Name"/> é vazio ou só espaço em branco.</summary>
public sealed class InvalidProductNameException : DomainException
{
    public InvalidProductNameException()
        : base("Product name must not be empty.")
    {
    }
}
