namespace OrderService.Api.Contracts;

/// <summary>Payload de <c>POST /auth/token</c>.</summary>
public sealed record AuthTokenRequest(string? Username, string? Password);
