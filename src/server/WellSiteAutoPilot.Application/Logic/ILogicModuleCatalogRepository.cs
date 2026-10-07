using WellSiteAutoPilot.Domain.Logic;

namespace WellSiteAutoPilot.Application.Logic;

public interface ILogicModuleCatalogRepository
{
    Task<InstalledLogicModule?> GetAsync(
        string moduleId,
        string version,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<InstalledLogicModule>> ListAsync(
        string? moduleId,
        int limit,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        InstalledLogicModule logicModule,
        CancellationToken cancellationToken = default);
}
