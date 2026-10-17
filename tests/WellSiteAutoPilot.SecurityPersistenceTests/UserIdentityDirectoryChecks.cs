using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Security;

internal static class UserIdentityDirectoryChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options)
    {
        var identity = "CORP\\identity-" + Guid.NewGuid().ToString("N");
        var key = "NAME:" + identity;
        var results = await Task.WhenAll(ResolveAsync("First"), ResolveAsync("First"));
        var id = results[0].Id;
        await using var setup = new WellSiteAutoPilotDbContext(options);
        if (results.Any(item => item.Id != id) || await setup.Users.CountAsync() != 1 ||
            await setup.AuditEvents.CountAsync(item => item.TargetId == id.ToString() && item.Action == "security.user.provisioned") != 1 ||
            await setup.UserRoles.AnyAsync() || await setup.AccessGrants.AnyAsync() || await setup.UserAssetScopes.AnyAsync())
            throw new InvalidOperationException("Identity-only provisioning did not preserve atomic, authority-free single identity creation.");
        // A customer label in the old table must not make the new identity
        // directory parse a fixed ApplicationRole enum or confer authority.
        var oldLabel = new UserRoleEntity { UserId = id, Role = "Customer Technician" };
        setup.UserRoles.Add(oldLabel);
        await setup.SaveChangesAsync();
        var renamed = await ResolveAsync("Updated display");
        var service = new UserIdentityService(new UserAccessRepository(setup), TimeProvider.System);
        if (renamed.Id != id || renamed.DisplayName != "Updated display" ||
            !string.Equals((await service.GetRequiredAsync(id)).IdentityKey, key, StringComparison.OrdinalIgnoreCase) ||
            (await service.ListAsync(1)).Single().Id != id || await setup.AccessGrants.AnyAsync())
            throw new InvalidOperationException("Identity directory coupled customer role labels to identity resolution or authority.");
        var repeated = new UserIdentityService(new UserAccessRepository(setup), TimeProvider.System);
        if (!(await repeated.ResolveAuthenticatedAsync(key, identity, "Updated display")).IsActive)
            throw new InvalidOperationException("Active identity fixture was not initialized.");
        await setup.Users.Where(item => item.Id == id).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
        if ((await ResolveAsync("Updated display")).IsActive)
            throw new InvalidOperationException("Resolving a disabled identity reactivated it.");
        var disabled = await repeated.ResolveAuthenticatedAsync(key, identity, "Disabled display refresh");
        if (disabled.IsActive || disabled.DisplayName != "Disabled display refresh" ||
            (await repeated.ResolveAuthenticatedAsync(key, identity, "Disabled display refresh")).IsActive ||
            await setup.Users.AsNoTracking().AnyAsync(item => item.Id == id && item.IsActive))
            throw new InvalidOperationException("Tracked identity reuse hid a deactivation or metadata refresh reactivated the account.");
        await setup.UserRoles.Where(item => item.UserId == id).ExecuteDeleteAsync();
        await setup.AuditEvents.Where(item => item.TargetId == id.ToString()).ExecuteDeleteAsync();
        await setup.Users.Where(item => item.Id == id).ExecuteDeleteAsync();
        Console.WriteLine("Identity-only provisioning/directory preserves stable identity, concurrent atomic audit, disabled state and open customer labels without legacy authority.");

        async Task<UserIdentity> ResolveAsync(string displayName)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            return await new UserIdentityService(new UserAccessRepository(db), TimeProvider.System)
                .ResolveAuthenticatedAsync(key, identity, displayName);
        }
    }
}
