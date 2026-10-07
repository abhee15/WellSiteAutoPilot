using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;

namespace WellSiteAutoPilot.Persistence.Logic;

public sealed class LogicModuleCatalogRepository(
    WellSiteAutoPilotDbContext dbContext) : ILogicModuleCatalogRepository
{
    public async Task<InstalledLogicModule?> GetAsync(
        string moduleId,
        string version,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.LogicModules
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.ModuleId == moduleId && item.Version == version,
                cancellationToken);

        return entity is null ? null : ToDomain(entity);
    }

    public async Task<IReadOnlyCollection<InstalledLogicModule>> ListAsync(
        string? moduleId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.LogicModules.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(moduleId))
        {
            query = query.Where(item => item.ModuleId == moduleId);
        }

        return (await query
            .OrderBy(item => item.ModuleId)
            .ThenByDescending(item => item.InstalledAtUtc)
            .Take(limit)
            .ToArrayAsync(cancellationToken))
            .Select(ToDomain)
            .ToArray();
    }

    public async Task AddAsync(
        InstalledLogicModule logicModule,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logicModule);

        dbContext.LogicModules.Add(new LogicModuleCatalogEntity
        {
            Id = logicModule.Id,
            ModuleId = logicModule.ModuleId,
            Version = logicModule.Version,
            DisplayName = logicModule.DisplayName,
            Publisher = logicModule.Publisher,
            Runtime = logicModule.Runtime.ToString(),
            ExecutionProfile = logicModule.ExecutionProfile.ToString(),
            ManifestJson = logicModule.ManifestJson,
            PackageSha256 = logicModule.PackageSha256,
            TrustStatus = logicModule.TrustStatus.ToString(),
            IsEnabled = logicModule.IsEnabled,
            InstalledAtUtc = logicModule.InstalledAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static InstalledLogicModule ToDomain(
        LogicModuleCatalogEntity entity) => new(
            entity.Id,
            entity.ModuleId,
            entity.Version,
            entity.DisplayName,
            entity.Publisher,
            Enum.Parse<LogicRuntimeKind>(entity.Runtime),
            Enum.Parse<ExecutionProfile>(entity.ExecutionProfile),
            entity.ManifestJson,
            entity.PackageSha256,
            Enum.Parse<LogicModuleTrustStatus>(entity.TrustStatus),
            entity.IsEnabled,
            entity.InstalledAtUtc);
}
