using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Security;

internal static class AuthorizedSecurityDirectoryChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid directAdministrator,
        AccessSubject groupAdministrator, Guid administratorGrant, Guid customRole, Guid inactiveUser)
    {
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var direct = new AccessSubject(directAdministrator);
        var target = AccessGrantSubject.ForUser(directAdministrator);
        var groupTarget = AccessGrantSubject.ForWindowsGroup(groupAdministrator.WindowsGroups!.GroupSids.Single());
        var protectedRole = await setup.AccessGrants.Where(item => item.Id == administratorGrant).Select(item => item.RoleId).SingleAsync();
        // Directory metadata must never invoke the fixed ApplicationRole parser.
        setup.UserRoles.Add(new UserRoleEntity { UserId = groupAdministrator.UserId, Role = "Customer Technician" });
        await setup.SaveChangesAsync();
        setup.ChangeTracker.Clear();
        foreach (var actor in new[] { direct, groupAdministrator })
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            var directory = new SecurityDirectoryRepository(db, TimeProvider.System);
            var users = await directory.ListUsersAuthorizedAsync(actor, 500);
            if (!users.Any(item => item.Id == inactiveUser && !item.IsActive) ||
                !users.Any(item => item.Id == groupAdministrator.UserId) ||
                (await directory.ListUsersAuthorizedAsync(actor, 1)).Count != 1)
                throw new InvalidOperationException("Protected directory lost disabled/custom-role identities or paging.");
            var roles = await directory.ListRolesAuthorizedAsync(actor, 500);
            if (!roles.Any(item => item.Id == customRole && item.Permissions.Contains(WellSitePermissions.SecurityManage)) ||
                (await directory.GetRoleAuthorizedAsync(actor, protectedRole))?.IsSystem != true ||
                await directory.GetRoleAuthorizedAsync(actor, Guid.NewGuid()) is not null)
                throw new InvalidOperationException("Protected role directory lost permission hydration/detail semantics.");
            if ((await directory.ListGrantsAuthorizedAsync(actor, target)).Count != 2 ||
                (await directory.ListGrantsAuthorizedAsync(actor, groupTarget)).Count != 1 ||
                (await directory.ListGrantsAuthorizedAsync(actor, AccessGrantSubject.ForUser(inactiveUser))).Count != 0)
                throw new InvalidOperationException("Protected assignment directory lost user/group or disabled-user inspection.");
            await ExpectAsync(() => directory.ListGrantsAuthorizedAsync(actor, AccessGrantSubject.ForUser(Guid.NewGuid())), FailureKind.NotFound);
            foreach (var limit in new[] { 0, 501 })
            {
                await ExpectAsync(() => directory.ListUsersAuthorizedAsync(actor, limit), FailureKind.Validation);
                await ExpectAsync(() => directory.ListRolesAuthorizedAsync(actor, limit), FailureKind.Validation);
            }
        }
        var expired = new AccessSubject(groupAdministrator.UserId, new(groupAdministrator.UserId,
            groupAdministrator.WindowsGroups.GroupSids, DateTimeOffset.UtcNow.AddMinutes(-2)));
        // The same user has a custom security.manage grant, which is not the
        // protected administrator privilege class. A target SID is no proof.
        foreach (var actor in new[] { new AccessSubject(groupAdministrator.UserId), expired, new AccessSubject(Guid.NewGuid()) })
            await ExpectAllDeniedAsync(actor);
        var expiryClock = new AdjustableClock(groupAdministrator.WindowsGroups.ObservedAtUtc);
        var expiryOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options)
            .AddInterceptors(new ExpireDuringRoleRead(expiryClock)).Options;
        await using (var expiring = new WellSiteAutoPilotDbContext(expiryOptions))
            await ExpectAsync(() => new SecurityDirectoryRepository(expiring, expiryClock)
                .ListRolesAuthorizedAsync(groupAdministrator, 100), FailureKind.Authorization);
        var retired = await setup.AccessGrants.Where(item => item.UserId == directAdministrator && item.ScopeKind == "AllAssets")
            .Select(item => item.Id).SingleAsync();
        await setup.AccessGrants.Where(item => item.Id == retired).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
        await using (var history = new WellSiteAutoPilotDbContext(options))
            if (!(await new SecurityDirectoryRepository(history, TimeProvider.System).ListGrantsAuthorizedAsync(direct, target))
                .Any(item => item.Id == retired && !item.IsActive))
                throw new InvalidOperationException("Retired assignments disappeared from protected directory inspection.");
        await setup.AccessGrants.Where(item => item.Id == retired).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, true));
        await setup.AccessGrants.Where(item => item.Id == administratorGrant)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
        await ExpectAllDeniedAsync(direct);
        await setup.AccessGrants.Where(item => item.Id == administratorGrant)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, true));

        await using (var weak = new WellSiteAutoPilotDbContext(options))
        {
            await using var transaction = await weak.Database.BeginTransactionAsync();
            try
            {
                await new SecurityDirectoryRepository(weak, TimeProvider.System).ListRolesAuthorizedAsync(direct, 100);
                throw new InvalidOperationException("Directory hydration accepted a weak caller transaction.");
            }
            catch (InvalidOperationException exception) when (exception.Message == "Authorized security directory reads require a stable database snapshot.") { }
        }

        var pause = new PauseRoleRead();
        var pausedOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(pause).Options;
        await using (var reader = new WellSiteAutoPilotDbContext(pausedOptions))
        {
            var read = new SecurityDirectoryRepository(reader, TimeProvider.System).ListRolesAuthorizedAsync(direct, 500);
            try
            {
                await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await setup.RolePermissions.Where(item => item.RoleId == customRole).ExecuteDeleteAsync();
                setup.RolePermissions.Add(new RolePermissionEntity { RoleId = customRole, Permission = WellSitePermissions.AssetsRead });
                await setup.SaveChangesAsync();
                setup.ChangeTracker.Clear();
                await setup.AccessGrants.Where(item => item.Id == administratorGrant)
                    .ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
            }
            finally { pause.Release.TrySetResult(); }
            if (!(await read).Single(item => item.Id == customRole).Permissions.SequenceEqual([WellSitePermissions.SecurityManage]))
                throw new InvalidOperationException("Directory permission hydration escaped its authorized snapshot.");
        }
        await ExpectAllDeniedAsync(direct);
        await setup.AccessGrants.Where(item => item.Id == administratorGrant)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, true));
        await using (var fresh = new WellSiteAutoPilotDbContext(options))
            if (!(await new SecurityDirectoryRepository(fresh, TimeProvider.System).GetRoleAuthorizedAsync(direct, customRole))!
                .Permissions.SequenceEqual([WellSitePermissions.AssetsRead]))
                throw new InvalidOperationException("Fresh directory read cached pre-change permissions.");
        await setup.RolePermissions.Where(item => item.RoleId == customRole).ExecuteDeleteAsync();
        setup.RolePermissions.Add(new RolePermissionEntity { RoleId = customRole, Permission = WellSitePermissions.SecurityManage });
        await setup.SaveChangesAsync();
        await setup.UserRoles.Where(item => item.UserId == groupAdministrator.UserId).ExecuteDeleteAsync();
        Console.WriteLine("Protected user/role/grant directory supports direct/group administrators, bypasses legacy role parsing, preserves a hydration snapshot and denies fresh reads after revocation.");

        async Task ExpectAllDeniedAsync(AccessSubject actor)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            var directory = new SecurityDirectoryRepository(db, TimeProvider.System);
            await ExpectAsync(() => directory.ListUsersAuthorizedAsync(actor, 100), FailureKind.Authorization);
            await ExpectAsync(() => directory.ListRolesAuthorizedAsync(actor, 100), FailureKind.Authorization);
            await ExpectAsync(() => directory.GetRoleAuthorizedAsync(actor, protectedRole), FailureKind.Authorization);
            await ExpectAsync(() => directory.ListGrantsAuthorizedAsync(actor, target), FailureKind.Authorization);
            await ExpectAsync(() => directory.ListGrantsAuthorizedAsync(actor, groupTarget), FailureKind.Authorization);
        }
    }

    private static async Task ExpectAsync(Func<Task> action, FailureKind kind)
    {
        try { await action(); throw new InvalidOperationException("Invalid directory request was accepted."); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == kind) { }
    }

    private sealed class PauseRoleRead : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (IsRoleRead(command))
            {
                Reached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }

    private static bool IsRoleRead(DbCommand command) =>
        !command.CommandText.StartsWith("SELECT EXISTS", StringComparison.Ordinal) &&
        command.CommandText.Contains("FROM security.role_definitions AS", StringComparison.Ordinal);

    private sealed class AdjustableClock(DateTimeOffset observed) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = observed;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ExpireDuringRoleRead(AdjustableClock clock) : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (IsRoleRead(command)) clock.Now = clock.Now.Add(VerifiedWindowsGroupMembership.MaximumAge).AddTicks(1);
            return ValueTask.FromResult(result);
        }
    }
}
