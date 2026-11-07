using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Connections;
using WellSiteAutoPilot.Persistence.Security;

internal static class AssetOperationSourceChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid assetTypeId)
    {
        await using var db = new WellSiteAutoPilotDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var now = DateTimeOffset.UtcNow;
        var user = Guid.NewGuid();
        var identity = "operation-source-" + user.ToString("N");
        db.Users.Add(new UserEntity { Id = user, IdentityKey = identity, IdentityName = identity, NormalizedIdentityName = identity,
            IsActive = true, CreatedAtUtc = now, LastSeenAtUtc = now });
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        foreach (var id in new[] { owner, other })
            db.Connections.Add(new ConnectionEntity { Id = id, Name = id.ToString(), NormalizedName = id.ToString(), AdapterKey = "foresite",
                SettingsVersion = 1, IsEnabled = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
        var allowed = Guid.NewGuid();
        var denied = Guid.NewGuid();
        var group = Guid.NewGuid();
        foreach (var id in new[] { allowed, denied, group })
            db.Assets.Add(new AssetEntity { Id = id, AssetTypeId = assetTypeId, Name = id.ToString(), OwnerConnectionId = owner,
                IsActive = true, CreatedAtUtc = now });
        var readRole = Role(WellSitePermissions.AssetsRead);
        var manageRole = Role(WellSitePermissions.AssetsManage);
        var readGrant = Grant(readRole, allowed, group);
        Grant(manageRole, denied); // Permission and scope from different grants must never be unioned.
        Binding(owner, allowed, "correct");
        Binding(other, allowed, "wrong-connection"); // Foreign binding cannot win ordering or leak into the response.
        Binding(owner, denied, "hidden");
        await db.SaveChangesAsync();
        var repository = new AssetRepository(db);
        var subject = new AccessSubject(user);
        var source = await repository.GetConnectionSourceAuthorizedAsync(subject, allowed, owner);
        Check(source is { IsActive: true, Bindings.Count: 1 } && source.Bindings[0].ExternalId == "correct", "Exact owner/source SQL projection failed");
        Check(await repository.GetConnectionSourceAuthorizedAsync(subject, allowed, other) is null, "Wrong connection ownership accepted");
        Check(await repository.GetConnectionSourceAuthorizedAsync(subject, denied, owner) is null, "Read permission borrowed another grant's scope");
        Check(await repository.GetConnectionSourceAuthorizedAsync(subject, Guid.NewGuid(), owner) is null, "Unknown asset accepted");
        Check((await repository.GetConnectionSourceAuthorizedAsync(subject, group, owner))?.Bindings.Count == 0, "Hierarchy node invented a binding");
        Binding(owner, allowed, "second");
        Binding(owner, allowed, "third");
        await db.SaveChangesAsync();
        Check((await repository.GetConnectionSourceAuthorizedAsync(subject, allowed, owner))?.Bindings.Count == 2, "Ambiguous source scan was not bounded to two");
        await db.AccessGrants.Where(item => item.Id == readGrant).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
        Check(await repository.GetConnectionSourceAuthorizedAsync(subject, allowed, owner) is null, "Revoked grant reused");
        await db.AccessGrants.Where(item => item.Id == readGrant).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, true));
        await db.Users.Where(item => item.Id == user).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
        Check(await repository.GetConnectionSourceAuthorizedAsync(subject, allowed, owner) is null, "Inactive user authorized");
        // Fixture rolls back, including its intentionally inconsistent cross-connection binding.

        Guid Role(string permission)
        {
            var id = Guid.NewGuid();
            db.RoleDefinitions.Add(new RoleDefinitionEntity { Id = id, Key = id.ToString("N"), Name = id.ToString(), NormalizedName = id.ToString(),
                IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.RolePermissions.Add(new RolePermissionEntity { RoleId = id, Permission = permission });
            return id;
        }
        Guid Grant(Guid role, params Guid[] assets)
        {
            var id = Guid.NewGuid();
            db.AccessGrants.Add(new AccessGrantEntity { Id = id, SubjectKind = "User", UserId = user, RoleId = role, ScopeKind = "ExactAssets",
                IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            foreach (var asset in assets) db.AccessGrantAssets.Add(new AccessGrantAssetEntity { GrantId = id, AssetId = asset });
            return id;
        }
        void Binding(Guid connection, Guid asset, string external) => db.SourceBindings.Add(new SourceBindingEntity {
            Id = Guid.NewGuid(), ConnectionId = connection, AssetId = asset, ResourceKind = "foresite.well", ExternalId = external, CreatedAtUtc = now });
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
