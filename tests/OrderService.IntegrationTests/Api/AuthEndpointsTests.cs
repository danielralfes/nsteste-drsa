using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OrderService.Api.Auth;
using OrderService.Api.Contracts;
using OrderService.Domain.Common;
using OrderService.Domain.Products;
using OrderService.Infrastructure.Persistence;

namespace OrderService.IntegrationTests.Api;

/// <summary>Testes de integração HTTP de autenticação/autorização JWT: emissão de token, rejeição de credenciais inválidas, bloqueio de <c>/orders/*</c> sem token ou com token inválido.</summary>
[Collection(ApiCollection.Name)]
public sealed class AuthEndpointsTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public AuthEndpointsTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Token_WithValidCredentials_Returns200WithParseableJwtCarryingUsernameAsSub()
    {
        var response = await _client.PostAsJsonAsync(
            "/auth/token",
            new AuthTokenRequest(ApiFactory.DevUsername, ApiFactory.DevPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.True(body.ExpiresAt > DateTime.UtcNow);

        var handler = new JwtSecurityTokenHandler();
        Assert.True(handler.CanReadToken(body.AccessToken));

        var token = handler.ReadJwtToken(body.AccessToken);
        var sub = token.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value;
        Assert.Equal(ApiFactory.DevUsername, sub);

        // Nenhum dado sensível (senha em texto plano, hash PBKDF2 etc.) pode
        // vazar no payload do JWT — o payload é só Base64URL, não
        // criptografado, então qualquer claim aqui é legível por quem tem o token.
        var expectedClaimTypes = new[]
        {
            JwtRegisteredClaimNames.Sub,
            JwtRegisteredClaimNames.Jti,
            JwtRegisteredClaimNames.Exp,
            JwtRegisteredClaimNames.Iss,
            JwtRegisteredClaimNames.Aud,
            JwtRegisteredClaimNames.Nbf,
            JwtRegisteredClaimNames.Iat,
        };
        foreach (var claim in token.Claims)
        {
            Assert.Contains(claim.Type, expectedClaimTypes);
        }

        var rawPayloadJson = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(token.RawPayload));
        Assert.DoesNotContain(ApiFactory.DevPassword, rawPayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Dev@123456", rawPayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", rawPayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("210000", rawPayloadJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ApiFactory.DevUsername, "wrong-password")]
    [InlineData("unknown-user", ApiFactory.DevPassword)]
    [InlineData("", "")]
    public async Task Token_WithInvalidCredentials_Returns401ProblemDetails(string username, string password)
    {
        var response = await _client.PostAsJsonAsync("/auth/token", new AuthTokenRequest(username, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(401, problem!.Status);
    }

    [Fact]
    public async Task Orders_WithoutAuthorizationHeader_Returns401()
    {
        var response = await _client.GetAsync("/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Orders_WithMalformedToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-valid-jwt");

        var response = await _client.GetAsync("/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_PostConfirmCancel_WithoutToken_AllReturn401()
    {
        var randomId = Guid.NewGuid();

        var createResponse = await _client.PostAsJsonAsync(
            "/orders",
            new CreateOrderRequest(Guid.NewGuid(), Currency.BRL, new[] { new CreateOrderItemRequest(Guid.NewGuid(), 1) }));
        var confirmResponse = await _client.PostAsync($"/orders/{randomId}/confirm", content: null);
        var cancelResponse = await _client.PostAsync($"/orders/{randomId}/cancel", content: null);
        var getResponse = await _client.GetAsync($"/orders/{randomId}");
        var listResponse = await _client.GetAsync("/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, confirmResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, cancelResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
    }

    [Fact]
    public async Task Orders_WithExpiredToken_Returns401()
    {
        // Gera o token manualmente com a mesma chave/issuer/audience da API
        // (lidas via DI, não hardcoded), mas com `exp` no passado — assim dá
        // pra simular um token expirado sem esperar os 60 minutos reais.
        using var scope = _factory.Services.CreateScope();
        var jwtOptions = scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value;

        var expiredToken = BuildToken(
            jwtOptions.Issuer,
            jwtOptions.Audience,
            jwtOptions.Key,
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await _client.GetAsync("/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Orders_WithTokenSignedByDifferentKey_Returns401()
    {
        // Token "forjado": issuer/audience/claims válidos, mas assinado com
        // uma chave diferente da configurada na API. Simula um atacante
        // montando um token sem conhecer o segredo — tem que ser rejeitado
        // por falha de assinatura.
        using var scope = _factory.Services.CreateScope();
        var jwtOptions = scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value;

        const string wrongKey = "this-is-a-completely-different-signing-key-not-configured-anywhere";

        var forgedToken = BuildToken(
            jwtOptions.Issuer,
            jwtOptions.Audience,
            wrongKey,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(30));

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        var response = await _client.GetAsync("/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string BuildToken(string issuer, string audience, string signingKey, DateTime notBefore, DateTime expires)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, ApiFactory.DevUsername),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task FullFlow_ObtainTokenThenCreateOrder_Succeeds()
    {
        var authenticatedClient = await _factory.CreateAuthenticatedClientAsync();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderServiceDbContext>();
        var product = new Product($"Auth flow product {Guid.NewGuid()}", new Money(10m, Currency.BRL));
        context.Products.Add(product);
        context.Stocks.Add(new Stock(product.Id, 10));
        await context.SaveChangesAsync();

        var request = new CreateOrderRequest(
            Guid.NewGuid(),
            Currency.BRL,
            new[] { new CreateOrderItemRequest(product.Id, 1) });

        var response = await authenticatedClient.PostAsJsonAsync("/orders", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
