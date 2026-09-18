using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OrderService.Infrastructure.Persistence;

/// <summary>
/// Auto-migrate no startup (decisions.md seção 7). O wiring de verdade —
/// chamar isso antes do <c>app.Run()</c> no <c>Program.cs</c> — é
/// responsabilidade da Fatia 4; aqui só deixamos o método pronto e testável.
/// </summary>
public static class MigrationExtensions
{
    /// <summary>
    /// Aplica as migrations pendentes de <see cref="OrderServiceDbContext"/>,
    /// logando quantas e quais foram aplicadas.
    /// </summary>
    public static async Task ApplyMigrationsAsync(this IHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderServiceDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(MigrationExtensions));

        var pendingMigrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pendingMigrations.Count == 0)
        {
            logger.LogInformation("No pending migrations to apply.");
            return;
        }

        logger.LogInformation(
            "Applying {Count} pending migration(s): {Migrations}",
            pendingMigrations.Count,
            string.Join(", ", pendingMigrations));

        await context.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Successfully applied {Count} migration(s).", pendingMigrations.Count);
    }
}
