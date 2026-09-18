using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OrderService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace OrderService.IntegrationTests.TestSupport;

/// <summary>Sobe um Postgres descartável (Testcontainers) uma única vez, compartilhado entre todos os testes da collection "Database" — um container por teste seria lento demais.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("orderservice")
        .WithUsername("orderservice")
        .WithPassword("orderservice")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public OrderServiceDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OrderServiceDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        return new OrderServiceDbContext(options);
    }

    /// <summary>Variante usada por testes que contam quantas queries SQL uma operação dispara (ex.: prova de ausência de N+1 em <c>OrderRepository.ListAsync</c>).</summary>
    public OrderServiceDbContext CreateContext(QueryCountingInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<OrderServiceDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;

        return new OrderServiceDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Database";
}

/// <summary>Conta quantos comandos SQL foram disparados no <see cref="OrderServiceDbContext"/> em que foi registrado — só usado em testes, para provar ausência de N+1.</summary>
public sealed class QueryCountingInterceptor : DbCommandInterceptor
{
    private int _queryCount;

    public int QueryCount => _queryCount;

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Interlocked.Increment(ref _queryCount);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _queryCount);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
