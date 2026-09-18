using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Application.Abstractions;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Persistence.Repositories;
using OrderService.Infrastructure.Time;

namespace OrderService.Infrastructure;

/// <summary>
/// Composition root desta camada. Registra o <see cref="OrderServiceDbContext"/>,
/// os repositórios reais e <see cref="IClock"/>/<see cref="IUnitOfWork"/>.
/// </summary>
/// <remarks>
/// Os handlers de <c>OrderService.Application</c> não são registrados aqui —
/// eles não dependem de nada de Infrastructure, só das abstrações. Esse
/// registro fica pro composition root da API (Fatia 4), o que mantém esta
/// camada só com implementações de infraestrutura mesmo.
/// </remarks>
public static class DependencyInjection
{
    private const string ConnectionStringName = "OrderServiceDb";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' not found in configuration.");

        services.AddDbContext<OrderServiceDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
