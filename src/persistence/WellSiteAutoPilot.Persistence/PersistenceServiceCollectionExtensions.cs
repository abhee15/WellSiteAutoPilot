using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Persistence.Executions;

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
        services.AddScoped<IExecutionRepository, ExecutionRepository>();

        return services;
    }
}
