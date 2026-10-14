using WellSiteAutoPilot.Domain.Security;

namespace WellSiteAutoPilot.Application.Security;

public interface IRoleCatalogRepository
{
    Task<IReadOnlyCollection<RoleDefinition>> ListAsync(
        int limit, CancellationToken cancellationToken = default);

    Task<RoleDefinition?> GetAsync(
        Guid roleId, CancellationToken cancellationToken = default);

    Task<RoleDefinition> CreateAsync(
        RoleDefinition role, SecurityActorContext actor,
        CancellationToken cancellationToken = default);

    Task<RoleDefinition> UpdateAsync(
        Guid roleId, long expectedRevision, RoleMutationCommand mutation,
        SecurityActorContext actor, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}
