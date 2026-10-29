using WellSiteAutoPilot.Application.Audit;
using WellSiteAutoPilot.Domain.Security;

namespace WellSiteAutoPilot.Application.Security;

public sealed record SecurityAssetDirectoryEntry(Guid Id, string Name, bool IsActive);

/// <summary>An identity plus how many active role grants name this user directly.</summary>
public sealed record SecurityUserDirectoryEntry(UserIdentity User, int ActiveDirectGrantCount);

/// <summary>Protected administrator reads, independent of legacy role profiles.</summary>
public interface ISecurityDirectoryRepository
{
    Task<IReadOnlyCollection<DirectoryGroup>> ListAssignedGroupsAuthorizedAsync(AccessSubject administrator,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<SecurityAssetDirectoryEntry>> ListAssetsAuthorizedAsync(AccessSubject administrator,
        int offset, int limit, CancellationToken cancellationToken = default);
    /// <summary>
    /// Lists identities by name. With <paramref name="awaitingAccessOnly"/> it returns only active
    /// users that have no active direct grant, most recently seen first, so a waiting user is not
    /// hidden behind the page limit. Windows-group access is not visible to this query.
    /// </summary>
    Task<IReadOnlyCollection<SecurityUserDirectoryEntry>> ListUsersAuthorizedAsync(AccessSubject administrator, int limit,
        bool awaitingAccessOnly = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RoleDefinition>> ListRolesAuthorizedAsync(AccessSubject administrator, int limit,
        CancellationToken cancellationToken = default);
    Task<RoleDefinition?> GetRoleAuthorizedAsync(AccessSubject administrator, Guid roleId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<AccessGrant>> ListGrantsAuthorizedAsync(AccessSubject administrator, AccessGrantSubject target,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Newest-first, keyset-paged audit events for protected administrators. Filters run in SQL before
    /// the page is cut. Reading the audit trail is not itself audited.
    /// </summary>
    Task<AuditEventPage> ListAuditEventsAuthorizedAsync(AccessSubject administrator, AuditEventQuery query,
        CancellationToken cancellationToken = default);
}
