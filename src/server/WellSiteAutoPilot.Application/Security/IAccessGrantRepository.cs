using WellSiteAutoPilot.Domain.Security;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>
/// A new scoped assignment is not yet an active legacy authorization grant.
/// </summary>
public sealed record CreateAccessGrantCommand(
    Guid RoleId,
    AccessGrantScopeKind ScopeKind,
    IReadOnlyCollection<Guid> AssetIds,
    long ExpectedRoleRevision);

public interface IAccessGrantRepository
{
    Task<AccessGrant> CreateAuthorizedAsync(AccessGrantSubject subject, CreateAccessGrantCommand command,
        AccessSubject administrator, SecurityActorContext actor, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    Task<AccessGrant> DeactivateAuthorizedAsync(AccessGrantSubject subject, Guid grantId, long expectedRevision,
        AccessSubject administrator, SecurityActorContext actor, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AccessGrant>> ListAsync(AccessGrantSubject subject, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<AccessGrant>> ListForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        ListAsync(AccessGrantSubject.ForUser(userId), cancellationToken);

    Task<AccessGrant> DeactivateAsync(AccessGrantSubject subject, Guid grantId, long expectedRevision, SecurityActorContext actor, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    Task<AccessGrant> DeactivateForUserAsync(Guid userId, Guid grantId, long expectedRevision, SecurityActorContext actor, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        DeactivateAsync(AccessGrantSubject.ForUser(userId), grantId, expectedRevision, actor, nowUtc, cancellationToken);

    Task<AccessGrant> CreateAsync(AccessGrantSubject subject, CreateAccessGrantCommand command, SecurityActorContext actor, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    Task<AccessGrant> CreateForUserAsync(Guid userId, CreateAccessGrantCommand command, SecurityActorContext actor, DateTimeOffset nowUtc, CancellationToken cancellationToken = default) =>
        CreateAsync(AccessGrantSubject.ForUser(userId), command, actor, nowUtc, cancellationToken);
}
