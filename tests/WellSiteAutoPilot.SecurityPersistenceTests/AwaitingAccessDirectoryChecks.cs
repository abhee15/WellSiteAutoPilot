using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Security;

internal static partial class AuthorizedSecurityDirectoryChecks
{
    /// <summary>
    /// The waiting-for-access queue counts only active direct grants to active roles, never
    /// hides a waiting user behind the page limit and never lists inactive identities.
    /// </summary>
    private static async Task CheckAwaitingAccessAsync(
        DbContextOptions<WellSiteAutoPilotDbContext> options, AccessSubject administrator)
    {
        var now = DateTimeOffset.UtcNow;
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var activeRole = Guid.NewGuid();
        var inactiveRole = Guid.NewGuid();
        foreach (var (id, active) in new[] { (activeRole, true), (inactiveRole, false) })
        {
            setup.RoleDefinitions.Add(new RoleDefinitionEntity
            {
                Id = id, Key = id.ToString("N"), Name = id.ToString("N"),
                NormalizedName = id.ToString("N").ToUpperInvariant(), IsActive = active,
                Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            });
            setup.RolePermissions.Add(new RolePermissionEntity { RoleId = id, Permission = WellSitePermissions.AssetsRead });
        }

        // Last-seen times sit in the future so these rows outrank every other fixture user.
        var inactiveIdentity = User(32, active: false);
        var granted = User(31, active: true);
        var waitingNewest = User(30, active: true);
        var waitingRetiredGrant = User(29, active: true);
        var waitingInactiveRole = User(28, active: true);
        Grant(granted, activeRole, active: true);
        Grant(waitingRetiredGrant, activeRole, active: false);
        Grant(waitingInactiveRole, inactiveRole, active: true);
        await setup.SaveChangesAsync();
        setup.ChangeTracker.Clear();

        try
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            var directory = new SecurityDirectoryRepository(db, TimeProvider.System);
            var waiting = await directory.ListUsersAuthorizedAsync(administrator, 3, awaitingAccessOnly: true);
            if (!waiting.Select(entry => entry.User.Id).SequenceEqual([waitingNewest, waitingRetiredGrant, waitingInactiveRole]) ||
                waiting.Any(entry => entry.ActiveDirectGrantCount != 0))
                throw new InvalidOperationException(
                    "Awaiting-access queue must list active users without an active direct grant to an active role, newest first, within the limit.");

            var everyone = await directory.ListUsersAuthorizedAsync(administrator, 500);
            int Count(Guid id) => everyone.Single(entry => entry.User.Id == id).ActiveDirectGrantCount;
            if (Count(granted) != 1 || Count(waitingNewest) != 0 || Count(waitingRetiredGrant) != 0 ||
                Count(waitingInactiveRole) != 0 || everyone.All(entry => entry.User.Id != inactiveIdentity))
                throw new InvalidOperationException("Directory grant counts ignored grant or role state.");

            await ExpectAsync(() => directory.ListUsersAuthorizedAsync(administrator, 0, awaitingAccessOnly: true),
                FailureKind.Validation);
            await ExpectAsync(() => directory.ListUsersAuthorizedAsync(new AccessSubject(Guid.NewGuid()), 10, awaitingAccessOnly: true),
                FailureKind.Authorization);
        }
        finally
        {
            var users = new[] { inactiveIdentity, granted, waitingNewest, waitingRetiredGrant, waitingInactiveRole };
            var roles = new[] { activeRole, inactiveRole };
            await setup.AccessGrants.Where(grant => grant.UserId != null && users.Contains(grant.UserId.Value)).ExecuteDeleteAsync();
            await setup.Users.Where(user => users.Contains(user.Id)).ExecuteDeleteAsync();
            await setup.RolePermissions.Where(permission => roles.Contains(permission.RoleId)).ExecuteDeleteAsync();
            await setup.RoleDefinitions.Where(role => roles.Contains(role.Id)).ExecuteDeleteAsync();
        }

        Guid User(int daysAhead, bool active)
        {
            var id = Guid.NewGuid();
            setup.Users.Add(new UserEntity
            {
                Id = id, IdentityKey = $"awaiting:{id:N}", IdentityName = $"AWAITING\\{id:N}",
                NormalizedIdentityName = $"AWAITING\\{id:N}".ToUpperInvariant(), IsActive = active,
                CreatedAtUtc = now, LastSeenAtUtc = now.AddDays(daysAhead)
            });
            return id;
        }

        void Grant(Guid user, Guid role, bool active) =>
            setup.AccessGrants.Add(new AccessGrantEntity
            {
                Id = Guid.NewGuid(), SubjectKind = "User", UserId = user, RoleId = role,
                ScopeKind = "DeploymentWide", IsActive = active, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            });
    }
}
