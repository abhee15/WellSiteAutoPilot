using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;

namespace WellSiteAutoPilot.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddWellSitePersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<WellSiteAutoPilotDbContext>(
            options => options.UseNpgsql(connectionString));
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<IExecutionRepository, ExecutionRepository>();
        services.AddScoped<IConfiguredLogicRepository, ConfiguredLogicRepository>();

        return services;
    }
}
