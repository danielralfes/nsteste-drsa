namespace OrderService.Api.Auth;

/// <summary>Parâmetros de emissão/validação do JWT. A chave de assinatura vem de configuração/variável de ambiente, nunca hardcoded.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Expiração do token em minutos, default 60. Sem refresh token nesta v1,
    /// então é o meio-termo entre não pedir login toda hora e não deixar um
    /// token vazado válido por tempo demais.
    /// </summary>
    public int ExpirationMinutes { get; set; } = 60;
}
