using WellSiteAutoPilot.Domain.Security;

namespace WellSiteAutoPilot.Application.Security;

public interface IUserAccessRepository
{
    Task<UserAccessProfile?> GetByIdentityAsync(
        string normalizedIdentityName,
        CancellationToken cancellationToken = default);

    Task<UserAccessProfile?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<UserAccessProfile>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<UserAccessProfile> UpsertAuthenticatedUserAsync(
        string identityName,
        string normalizedIdentityName,
        string? displayName,
        DateTimeOffset seenAtUtc,
        CancellationToken cancellationToken = default);

    Task ReplaceAccessAsync(
        Guid userId,
        IReadOnlyCollection<ApplicationRole> roles,
        IReadOnlyCollection<Guid> assetScopeIds,
        bool preserveLastAdministrator,
        CancellationToken cancellationToken = default);

    Task EnsureRoleAsync(
        Guid userId,
        ApplicationRole role,
        CancellationToken cancellationToken = default);

}
