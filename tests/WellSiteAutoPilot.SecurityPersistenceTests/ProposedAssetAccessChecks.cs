using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Security;

internal static class ProposedAssetAccessChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options,
        UserAccessProfile administrator, UserAccessProfile demoted, Guid assetA, Guid assetB, TimeProvider clock)
    {
        await using var db = new WellSiteAutoPilotDbContext(options);
        var now = clock.GetUtcNow();
        var userId = Guid.NewGuid();
        var identity = userId.ToString("N");
        db.Users.Add(new UserEntity { Id = userId, IdentityKey = identity, IdentityName = identity,
            NormalizedIdentityName = identity.ToUpperInvariant(), IsActive = true,
            CreatedAtUtc = now, LastSeenAtUtc = now });
        var writerRole = AddRole("Preview writer", [WellSitePermissions.AssetsRead, WellSitePermissions.AssetsManage]);
        var readerRole = AddRole("Preview reader", [WellSitePermissions.AssetsRead]);
        AddGrant(writerRole, assetA);
        AddGrant(readerRole, assetB);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var users = new UserAccessRepository(db);
        var administration = new AccessGrantAdministrationService(new AccessGrantRepository(db), users,
            new RoleCatalogRepository(db), clock);
        var service = new ProposedAssetAccessService(administration, users,
            new ProposedAssetAccessRepository(db), new ProposedAssetAccessAuditRepository(db), clock);
        var actor = new SecurityActorContext(administrator.Id, administrator.IdentityName, "proposed-assets-regression");
        var initialAudits = await db.AuditEvents.CountAsync();
        foreach (var denied in new[] { new SecurityActorContext(userId, identity, null),
            new SecurityActorContext(demoted.Id, demoted.IdentityName, null) })
            await RejectAsync(() => service.ListAsync(userId, WellSitePermissions.AssetsRead, 0, 1, denied), "SECURITY_ADMIN_REQUIRED");
        foreach (var invalid in new[] { (-1, 1), (1_000_001, 1), (0, 0), (0, 501) })
            await RejectAsync(() => service.ListAsync(userId, WellSitePermissions.AssetsRead, invalid.Item1, invalid.Item2, actor), "SECURITY_PREVIEW_QUERY_INVALID");
        foreach (var permission in new[] { "unknown", WellSitePermissions.SecurityManage })
            await RejectAsync(() => service.ListAsync(userId, permission, 0, 1, actor), "SECURITY_PREVIEW_QUERY_INVALID");
        await RejectAsync(() => service.ListAsync(Guid.Empty, WellSitePermissions.AssetsRead, 0, 1, actor), "SECURITY_PREVIEW_QUERY_INVALID");
        await RejectAsync(() => service.ListAsync(Guid.NewGuid(), WellSitePermissions.AssetsRead, 0, 1, actor), "SECURITY_USER_NOT_ACTIVE");
        if (await db.AuditEvents.CountAsync() != initialAudits)
            throw new InvalidOperationException("Rejected preview produced a success audit.");
        var expected = await db.Assets.Where(item => item.Id == assetA || item.Id == assetB)
            .OrderBy(item => item.Name).ThenBy(item => item.Id).Select(item => item.Id).ToArrayAsync();
        var pageA = await service.ListAsync(userId, WellSitePermissions.AssetsRead, 0, 1, actor);
        var pageB = await service.ListAsync(userId, WellSitePermissions.AssetsRead, 1, 1, actor);
        var write = await service.ListAsync(userId, WellSitePermissions.AssetsManage, 0, 1, actor);
        var empty = await service.ListAsync(userId, WellSitePermissions.AssetsRead, 2, 1, actor);
        if (!pageA.Assets.Concat(pageB.Assets).Select(item => item.Id).SequenceEqual(expected) ||
            write.Assets.Single().Id != assetA || empty.Assets.Count != 0 ||
            !pageA.IsDiagnosticOnly || pageA.UserId != userId || pageA.Permission != WellSitePermissions.AssetsRead ||
            pageA.Offset != 0 || pageA.Limit != 1)
            throw new InvalidOperationException("Preview lost grant-scope isolation, ordering, paging or diagnostic metadata.");
        var audits = await db.AuditEvents.AsNoTracking().Where(item => item.CorrelationId == actor.CorrelationId).ToArrayAsync();
        if (audits.Length != 4 || audits.Any(item => item.Action != "security.access.assets-previewed" ||
            item.ActorUserId != administrator.Id || item.ActorIdentity != actor.IdentityName ||
            item.TargetId != userId.ToString("D") || item.TargetType != "security-user" || item.OccurredAtUtc != now ||
            !item.DetailsJson.Contains("\"diagnosticOnly\":true", StringComparison.Ordinal)))
            throw new InvalidOperationException("Preview did not retain its administrator, query and correlation audit.");
        // No diagnostic response may be returned when its audit cannot persist.
        try
        {
            await service.ListAsync(userId, WellSitePermissions.AssetsRead, 0, 1,
                actor with { IdentityName = new string('x', 257) });
            throw new InvalidOperationException("Preview returned without its durable audit.");
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is PostgresException { SqlState: "22001" }) { }
        db.ChangeTracker.Clear();
        if (await db.AuditEvents.CountAsync() != initialAudits + 4)
            throw new InvalidOperationException("Failed preview audit left partial state.");
        await db.Users.Where(item => item.Id == userId).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, false));
        await RejectAsync(() => service.ListAsync(userId, WellSitePermissions.AssetsRead, 0, 1, actor), "SECURITY_USER_NOT_ACTIVE");
        if ((await users.GetAsync(userId)) is not { IsActive: false, Roles.Count: 0, AssetScopeIds.Count: 0 })
            throw new InvalidOperationException("Proposed preview modified live legacy access.");

        Guid AddRole(string name, string[] permissions)
        {
            var id = Guid.NewGuid();
            db.RoleDefinitions.Add(new RoleDefinitionEntity { Id = id, Key = id.ToString("N"), Name = name,
                NormalizedName = (name + id.ToString("N")).ToUpperInvariant(), Description = "",
                IsActive = true, IsSystem = false, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            foreach (var permission in permissions)
                db.RolePermissions.Add(new RolePermissionEntity { RoleId = id, Permission = permission });
            return id;
        }
        void AddGrant(Guid roleId, Guid assetId)
        {
            var id = Guid.NewGuid();
            db.AccessGrants.Add(new AccessGrantEntity { Id = id, SubjectKind = "User", UserId = userId,
                RoleId = roleId, ScopeKind = "ExactAssets", IsActive = true, Revision = 1,
                CreatedAtUtc = now, UpdatedAtUtc = now });
            db.AccessGrantAssets.Add(new AccessGrantAssetEntity { GrantId = id, AssetId = assetId });
        }
    }
    private static async Task RejectAsync(Func<Task> action, string code)
    {
        try { await action(); throw new InvalidOperationException($"Unsafe preview accepted: {code}."); }
        catch (WellSiteAutoPilotException exception) when (exception.Code == code) { }
    }
}
