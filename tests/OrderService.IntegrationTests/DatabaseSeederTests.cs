using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OrderService.Infrastructure.Persistence.Seed;
using OrderService.IntegrationTests.TestSupport;

namespace OrderService.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public sealed class DatabaseSeederTests
{
    private readonly PostgresFixture _fixture;

    public DatabaseSeederTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SeedAsync_CalledTwice_IsIdempotent_DoesNotDuplicateProducts()
    {
        await using var context = _fixture.CreateContext();

        await DatabaseSeeder.SeedAsync(context, NullLogger.Instance);
        var countAfterFirstRun = await context.Products.CountAsync();
        Assert.True(countAfterFirstRun >= 4);

        await DatabaseSeeder.SeedAsync(context, NullLogger.Instance);
        var countAfterSecondRun = await context.Products.CountAsync();

        Assert.Equal(countAfterFirstRun, countAfterSecondRun);
    }
}
