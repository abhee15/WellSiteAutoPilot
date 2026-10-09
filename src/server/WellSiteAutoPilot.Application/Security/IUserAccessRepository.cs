using WellSiteAutoPilot.Domain.Security;

namespace WellSiteAutoPilot.Application.Security;

public interface IUserAccessRepository
{
    Task<UserAccessProfile?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<UserAccessProfile>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<UserAccessProfile> UpsertAuthenticatedUserAsync(
        string identityKey,
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
        SecurityActorContext actor,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveUserInRoleAsync(
        ApplicationRole role,
        CancellationToken cancellationToken = default);

    Task EnsureRoleAsync(
        Guid userId,
        ApplicationRole role,
        SecurityActorContext actor,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken = default);

}
