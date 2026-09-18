namespace OrderService.Domain.Common;

/// <summary>Base para exceções de regra de negócio, não erros técnicos/infra.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message)
        : base(message)
    {
    }
}
