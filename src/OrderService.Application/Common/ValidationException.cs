namespace OrderService.Application.Common;

/// <summary>
/// Erro de validação de entrada na Application (ex.: <c>pageSize</c> acima do máximo em
/// <c>ListOrders</c>). Não é <see cref="OrderService.Domain.Common.DomainException"/>
/// porque não é invariante de entidade de domínio, é parâmetro de caso de uso fora do
/// intervalo aceito. Mapeamento HTTP sugerido: <c>400 Bad Request</c>.
/// </summary>
public sealed class ValidationException : Exception
{
    public ValidationException(string message)
        : base(message)
    {
    }
}
