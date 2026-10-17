using WellSiteAutoPilot.Domain.Security;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>Protected administrator reads, independent of legacy role profiles.</summary>
public interface ISecurityDirectoryRepository
{
    Task<IReadOnlyCollection<UserIdentity>> ListUsersAuthorizedAsync(AccessSubject administrator, int limit,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RoleDefinition>> ListRolesAuthorizedAsync(AccessSubject administrator, int limit,
        CancellationToken cancellationToken = default);
    Task<RoleDefinition?> GetRoleAuthorizedAsync(AccessSubject administrator, Guid roleId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<AccessGrant>> ListGrantsAuthorizedAsync(AccessSubject administrator, AccessGrantSubject target,
        CancellationToken cancellationToken = default);
}
