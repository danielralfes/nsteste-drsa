using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OrderService.Domain.Common;
using OrderService.Domain.Products;

namespace OrderService.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeder idempotente de catálogo/estoque (decisions.md seção 6). Não usa
/// <c>HasData</c> porque estoque é mutável em runtime. Roda no startup da
/// API depois de <see cref="MigrationExtensions.ApplyMigrationsAsync"/>; o
/// wiring no <c>Program.cs</c> fica pra Fatia 4.
/// </summary>
public static class DatabaseSeeder
{
    // Guids fixos pra dar pra checar "já existe" de forma determinística a
    // cada restart, sem duplicar nem resetar nada.
    private static readonly Guid NotebookId = Guid.Parse("00000000-0000-7000-8000-000000000001");
    private static readonly Guid MouseId = Guid.Parse("00000000-0000-7000-8000-000000000002");
    private static readonly Guid KeyboardId = Guid.Parse("00000000-0000-7000-8000-000000000003");
    private static readonly Guid MonitorId = Guid.Parse("00000000-0000-7000-8000-000000000004");

    public static async Task SeedAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderServiceDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseSeeder));

        await SeedAsync(context, logger, cancellationToken);
    }

    // SQLSTATE do Postgres pra unique_violation — usamos isso pra distinguir
    // "outra instância já semeou" de uma falha de escrita de verdade.
    private const string PostgresUniqueViolationSqlState = "23505";

    /// <summary>
    /// Sobrecarga que recebe o <see cref="OrderServiceDbContext"/> diretamente
    /// — usada pelos testes de integração, que não montam um <see cref="IHost"/>
    /// completo.
    /// </summary>
    public static async Task SeedAsync(OrderServiceDbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);

        var seedProducts = new[]
        {
            new { Id = NotebookId, Name = "Notebook", Price = 4599.90m, Available = 15 },
            new { Id = MouseId, Name = "Mouse", Price = 89.90m, Available = 200 },
            new { Id = KeyboardId, Name = "Teclado", Price = 249.90m, Available = 120 },
            new { Id = MonitorId, Name = "Monitor", Price = 1299.00m, Available = 40 },
        };

        var existingIds = await context.Products
            .Where(p => seedProducts.Select(sp => sp.Id).Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var missing = seedProducts.Where(sp => !existingIds.Contains(sp.Id)).ToList();

        if (missing.Count == 0)
        {
            logger.LogInformation("Seed data already present, nothing to insert.");
            return;
        }

        foreach (var seedProduct in missing)
        {
            var product = new Product(seedProduct.Id, seedProduct.Name, new Money(seedProduct.Price, Currency.BRL));
            var stock = new Stock(seedProduct.Id, seedProduct.Available);

            context.Products.Add(product);
            context.Stocks.Add(stock);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Se duas instâncias da API sobem ao mesmo tempo, as duas podem
            // fazer o SELECT "não existe" antes de qualquer INSERT — janela
            // de corrida (decisions.md seção 21.4). A segunda a gravar
            // esbarra na PK; tratamos isso como "já foi semeado por outra
            // instância" em vez de derrubar o startup.
            logger.LogWarning(
                ex,
                "Seed insert hit a unique constraint violation — another instance likely seeded the data concurrently. Continuing startup.");
            return;
        }

        logger.LogInformation(
            "Seeded {Count} product(s): {Names}.",
            missing.Count,
            string.Join(", ", missing.Select(p => p.Name)));
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresUniqueViolationSqlState };
    }
}
