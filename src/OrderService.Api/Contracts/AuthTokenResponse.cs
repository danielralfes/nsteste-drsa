namespace OrderService.Api.Contracts;

/// <summary>Resposta de sucesso de <c>POST /auth/token</c>.</summary>
public sealed record AuthTokenResponse(string AccessToken, DateTime ExpiresAt);
