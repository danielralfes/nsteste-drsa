namespace OrderService.Api.Auth;

/// <summary>Credenciais do usuário único semeado via configuração — não existe tabela de usuários nem cadastro no domínio (decisions.md seção 3).</summary>
/// <remarks>
/// <see cref="PasswordHash"/> nunca é a senha em texto plano, é o resultado
/// de <see cref="PasswordHasher.Hash"/> (PBKDF2/HMACSHA256), lido da seção
/// <c>Auth</c> de configuração. Em produção isso viria de um cofre de
/// segredos, não de um arquivo versionado — ver appsettings.Development.json
/// para o hash local e a senha em texto plano correspondente (só para teste).
/// </remarks>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
}
