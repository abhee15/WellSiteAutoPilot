using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Assets.Views;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Connections.Importing;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Recommendations;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Assets.Views;
using WellSiteAutoPilot.Persistence.Connections;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Logic;
using WellSiteAutoPilot.Persistence.Recommendations;
using WellSiteAutoPilot.Persistence.Security;

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
        services.AddScoped<IAssetDetailReader, AssetDetailReader>();
        services.AddScoped<IConnectionRepository, ConnectionRepository>();
        services.AddScoped<IAssetViewRepository, AssetViewRepository>();
        services.AddScoped<IConnectionImportRepository, ConnectionImportRepository>();
        services.AddScoped<ISourceChangeRepository, SourceChangeRepository>();
        services.AddScoped<IExecutionRepository, ExecutionRepository>();
        services.AddScoped<IExecutionReadRepository, ExecutionRepository>();
        services.AddScoped<IExecutionHistoryReader, ExecutionHistoryReader>();
        services.AddScoped<IConfiguredLogicRepository, ConfiguredLogicRepository>();
        services.AddScoped<ILogicModuleCatalogRepository, LogicModuleCatalogRepository>();
        services.AddScoped<IModulePackageStateRepository, LogicModuleCatalogRepository>();
        services.AddScoped<ILogicModulePackageRepository, LogicModuleCatalogRepository>();
        services.AddScoped<ILogicModuleApprovalRepository, LogicModuleCatalogRepository>();
        services.AddScoped<IRecommendationRepository, RecommendationRepository>();
        services.AddScoped<IUserAccessRepository, UserAccessRepository>();
        services.AddScoped<IUserIdentityRepository, UserAccessRepository>();
        services.AddScoped<IRequestAccessRepository, RequestAccessRepository>();
        services.AddScoped<IResourceActionRepository, ResourceActionRepository>();
        services.AddScoped<ISecurityDirectoryRepository, SecurityDirectoryRepository>();
        services.AddScoped<IGrantBootstrapRepository, GrantBootstrapRepository>();
        services.AddScoped<IRoleCatalogRepository, RoleCatalogRepository>();
        services.AddScoped<IAccessGrantRepository, AccessGrantRepository>();
        services.AddScoped<IProposedAssetAccessRepository, ProposedAssetAccessRepository>();
        services.AddScoped<IProposedAssetAccessAuditRepository, ProposedAssetAccessAuditRepository>();
        services.AddScoped<IScopedAccessInspectionAuditRepository, ScopedAccessInspectionAuditRepository>();

        services.AddScoped<ILegacyMigrationSnapshotRepository, LegacyMigrationSnapshotRepository>();
        services.AddScoped<IDeploymentMigrationSnapshotRepository, DeploymentMigrationSnapshotRepository>();
        services.AddScoped<ILegacyMigrationPreflightAuditRepository, LegacyMigrationPreflightAuditRepository>();

        services.AddScoped<ILegacyGrantMigrationRepository, LegacyGrantMigrationRepository>();

        return services;
    }
}
