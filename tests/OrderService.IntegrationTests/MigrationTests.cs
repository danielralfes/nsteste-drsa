using Microsoft.EntityFrameworkCore;
using OrderService.IntegrationTests.TestSupport;

namespace OrderService.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class MigrationTests
{
    private readonly PostgresFixture _fixture;

    public MigrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migrations_ApplyCleanly_AgainstEmptyDatabase()
    {
        // A migration já rodou em PostgresFixture.InitializeAsync (uma vez,
        // compartilhada pela collection) — aqui só confirmamos que não sobrou
        // migration pendente e que as tabelas existem e são consultáveis.
        await using var context = _fixture.CreateContext();

        var pending = await context.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);

        // Se o schema estivesse errado, essas queries triviais já lançariam.
        await context.Orders.CountAsync();
        await context.Products.CountAsync();
        await context.Stocks.CountAsync();
    }

    [Fact]
    public async Task Migrations_CreateOrderListingCompositeIndex_OnOrdersTable()
    {
        // Confirma no catálogo do Postgres (pg_indexes) que a migration
        // AddOrderListingIndexes realmente criou o índice composto
        // (CustomerId, Status, CreatedAt) na tabela "orders" — não basta a
        // migration existir no código, ela precisa ter sido aplicada certo.
        await using var context = _fixture.CreateContext();
        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT indexdef FROM pg_indexes WHERE tablename = 'orders' AND indexname = 'IX_orders_CustomerId_Status_CreatedAt'";

        await using var reader = await command.ExecuteReaderAsync();
        var found = await reader.ReadAsync();

        Assert.True(found, "Índice IX_orders_CustomerId_Status_CreatedAt não encontrado em pg_indexes para a tabela 'orders'.");

        var indexDefinition = reader.GetString(0);

        Assert.True(
            indexDefinition.Contains("CustomerId", StringComparison.OrdinalIgnoreCase)
                || indexDefinition.Contains("customer_id", StringComparison.OrdinalIgnoreCase),
            $"Definição do índice não contém CustomerId: {indexDefinition}");
        Assert.True(
            indexDefinition.Contains("Status", StringComparison.OrdinalIgnoreCase),
            $"Definição do índice não contém Status: {indexDefinition}");
        Assert.True(
            indexDefinition.Contains("CreatedAt", StringComparison.OrdinalIgnoreCase)
                || indexDefinition.Contains("created_at", StringComparison.OrdinalIgnoreCase),
            $"Definição do índice não contém CreatedAt: {indexDefinition}");
    }

    [Fact]
    public async Task Migrations_OrderItemsTable_HasIndexOnOrderIdForeignKey()
    {
        // IX_order_items_OrderId já existia desde a InitialCreate (FK padrão
        // do EF Core) — checa que está mesmo no schema aplicado, não só
        // presumido pela leitura da migration original.
        await using var context = _fixture.CreateContext();
        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT indexname FROM pg_indexes WHERE tablename = 'order_items' AND indexname = 'IX_order_items_OrderId'";

        await using var reader = await command.ExecuteReaderAsync();
        var found = await reader.ReadAsync();

        Assert.True(found, "Índice IX_order_items_OrderId não encontrado em pg_indexes para a tabela 'order_items'.");
    }
}
