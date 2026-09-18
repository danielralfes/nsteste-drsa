namespace OrderService.Application.Common;

/// <summary>
/// Base para "recurso solicitado não existe". Separada de
/// <see cref="OrderService.Domain.Common.DomainException"/> de propósito: não é violação
/// de invariante de negócio, é ausência de um recurso identificado por Id. Cada subtipo
/// pode mapear pra um status HTTP diferente, por isso não tem uma propriedade
/// "HttpStatusCode" única aqui — ver o mapeamento sugerido em cada subtipo.
/// </summary>
public abstract class NotFoundException : Exception
{
    protected NotFoundException(string message)
        : base(message)
    {
    }
}
