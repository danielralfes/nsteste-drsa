using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using OrderService.Api.Contracts;
using Testcontainers.PostgreSql;

namespace OrderService.IntegrationTests.Api;

/// <summary>
/// Sobe a API completa (<c>Program</c>) apontando para um Postgres
/// descartável (Testcontainers) — um único container compartilhado por toda
/// a collection "Api", mesma estratégia de <see cref="TestSupport.PostgresFixture"/>.
/// </summary>
/// <remarks>
/// O startup real de <c>Program.cs</c> (auto-migrate + seed) roda normalmente
/// aqui, a factory não pula esse passo — os testes de integração exercitam o
/// mesmo caminho de inicialização usado em produção.
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("orderservice")
        .WithUsername("orderservice")
        .WithPassword("orderservice")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting garante precedência mais alta que appsettings.json,
        // independente da ordem em que o WebApplicationBuilder monta suas
        // fontes de configuração. Tentamos ConfigureAppConfiguration com
        // AddInMemoryCollection antes e o appsettings.json (localhost:5432)
        // acabava sobrescrevendo por vir depois.
        builder.UseSetting("ConnectionStrings:OrderServiceDb", _container.GetConnectionString());
    }

    /// <summary>Credenciais de dev do usuário único (appsettings.Development.json), reaproveitadas aqui pra não duplicar em cada teste.</summary>
    public const string DevUsername = "admin";
    public const string DevPassword = "Dev@123456";

    /// <summary>Cria um <see cref="HttpClient"/> já autenticado (header Authorization com o token de <c>POST /auth/token</c>), pra uso nos testes de <c>/orders/*</c>.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/token",
            new AuthTokenRequest(DevUsername, DevPassword));
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token!.AccessToken);

        return client;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
