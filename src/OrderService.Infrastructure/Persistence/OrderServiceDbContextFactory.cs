using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrderService.Infrastructure.Persistence;

/// <summary>
/// Factory usada só em design-time pelas ferramentas do EF Core
/// (<c>dotnet ef migrations add</c>/<c>database update</c>) quando rodadas
/// direto contra o projeto <c>OrderService.Infrastructure</c>, que não tem
/// host/composition-root próprio (isso é a API). A connection string aqui
/// aponta pro Postgres do <c>docker-compose.yml</c> da raiz do repo e não é
/// usada em runtime — a API monta a dela via configuração (ver <see cref="DependencyInjection"/>).
/// </summary>
public sealed class OrderServiceDbContextFactory : IDesignTimeDbContextFactory<OrderServiceDbContext>
{
    public OrderServiceDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OrderServiceDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=orderservice;Username=orderservice;Password=orderservice");

        return new OrderServiceDbContext(optionsBuilder.Options);
    }
}
