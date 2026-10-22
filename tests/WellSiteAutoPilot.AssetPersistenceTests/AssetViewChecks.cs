using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets.Views;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Assets.Views;
using WellSiteAutoPilot.Persistence.Connections;
using WellSiteAutoPilot.Persistence.Security;

internal static class AssetViewChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid wellTypeId, Guid groupTypeId)
    {
        var now = DateTimeOffset.UtcNow;
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var firstConnection = Guid.NewGuid();
        var secondConnection = Guid.NewGuid();
        var legacyId = Guid.NewGuid();
        var wells = Enumerable.Range(0, 10_000).Select(index => new AssetEntity { Id = Guid.NewGuid(), AssetTypeId = wellTypeId,
            Name = $"View Well {index:D5}", OwnerConnectionId = firstConnection, IsActive = true, CreatedAtUtc = now }).ToArray();
        var foreign = new AssetEntity { Id = Guid.NewGuid(), AssetTypeId = wellTypeId, Name = "View Well 00000",
            OwnerConnectionId = secondConnection, IsActive = true, CreatedAtUtc = now };
        var group = new AssetEntity { Id = Guid.NewGuid(), AssetTypeId = groupTypeId, Name = "Navigation group",
            OwnerConnectionId = firstConnection, IsActive = true, CreatedAtUtc = now };
        var ids = wells.Select(well => well.Id).ToArray();
        await using var fixture = new WellSiteAutoPilotDbContext(options);
        try
        {
            foreach (var userId in new[] { firstUser, secondUser })
            {
                var identity = "view-fixture-" + userId.ToString("N");
                fixture.Users.Add(new UserEntity { Id = userId, IdentityKey = identity, IdentityName = identity,
                    NormalizedIdentityName = identity.ToUpperInvariant(), IsActive = true, CreatedAtUtc = now, LastSeenAtUtc = now });
            }
            fixture.RoleDefinitions.Add(new RoleDefinitionEntity { Id = roleId, Key = "view-fixture-" + roleId.ToString("N"),
                Name = roleId.ToString(), NormalizedName = roleId.ToString().ToUpperInvariant(), IsActive = true, Revision = 1,
                CreatedAtUtc = now, UpdatedAtUtc = now });
            foreach (var permission in new[] { WellSitePermissions.AssetsRead, WellSitePermissions.ConnectionsRead,
                WellSitePermissions.AssetViewsCreate, WellSitePermissions.AssetViewsEdit, WellSitePermissions.AssetViewsDelete })
                fixture.RolePermissions.Add(new RolePermissionEntity { RoleId = roleId, Permission = permission });
            foreach (var userId in new[] { firstUser, secondUser })
                foreach (var scope in new[] { "DeploymentWide", "AllAssets" })
                    fixture.AccessGrants.Add(new AccessGrantEntity { Id = Guid.NewGuid(), UserId = userId, RoleId = roleId,
                        ScopeKind = scope, IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            foreach (var connectionId in new[] { firstConnection, secondConnection })
                fixture.Connections.Add(new ConnectionEntity { Id = connectionId, Name = connectionId.ToString(),
                    NormalizedName = connectionId.ToString().ToUpperInvariant(), AdapterKey = "foresite", SettingsVersion = 1,
                    IsEnabled = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            fixture.Assets.AddRange(wells);
            fixture.Assets.AddRange(foreign, group);
            fixture.AssetViews.Add(new AssetViewEntity { Id = legacyId, OwnerUserId = firstUser, Name = "Legacy query",
                NormalizedName = "LEGACY QUERY", DefinitionJson = "{\"legacy\":true}", Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            await fixture.SaveChangesAsync();
            fixture.ChangeTracker.Clear();

            var firstSubject = new AccessSubject(firstUser);
            var secondSubject = new AccessSubject(secondUser);
            var firstActor = new SecurityActorContext(firstUser, "View fixture A", "view-fixture");
            var secondActor = new SecurityActorContext(secondUser, "View fixture B", "view-fixture");
            var created = await UseAsync(repo => repo.CreateAsync(firstSubject, firstActor, firstConnection, "All wells", ids));
            if (created.MemberCount != 10_000 || created.HasUnavailableMembers) throw new InvalidOperationException("Full membership did not persist.");
            var selection = await UseAsync(repo => repo.SelectionAsync(secondSubject, created.Id));
            if (selection.AssetIds.Count != 10_000 || !ids.ToHashSet().SetEquals(selection.AssetIds))
                throw new InvalidOperationException("Connection readers must see the complete selected ID set, independent of creator.");
            var tail = await UseAsync(repo => repo.MembersAsync(secondSubject, created.Id, null, 9_900, 100));
            if (tail.Count != 100 || tail[^1].Id != wells[^1].Id) throw new InvalidOperationException("Member paging lost wells beyond the first page.");
            var found = await UseAsync(repo => repo.MembersAsync(secondSubject, created.Id, "09999", 0, 100));
            if (found.Count != 1 || found[0].Id != wells[^1].Id) throw new InvalidOperationException("Member search must precede paging.");
            if ((await UseAsync(repo => repo.ListAsync(firstSubject))).Any(view => view.Id == legacyId) ||
                (await fixture.AssetViews.AsNoTracking().SingleAsync(view => view.Id == legacyId)).DefinitionJson != "{\"legacy\":true}")
                throw new InvalidOperationException("Legacy definitions must be preserved and excluded from executable Views.");
            await RejectAsync(() => UseAsync(repo => repo.CreateAsync(firstSubject, firstActor, firstConnection, "Invalid", [foreign.Id])));
            await RejectAsync(() => UseAsync(repo => repo.CreateAsync(firstSubject, firstActor, firstConnection, "Invalid", [group.Id])));
            await RejectAsync(() => UseAsync(repo => repo.CreateAsync(firstSubject, firstActor, firstConnection, "Invalid", [ids[0], ids[0]])));
            var updated = await UseAsync(repo => repo.UpdateAsync(secondSubject, secondActor, created.Id, 1, "Subset", ids.Skip(1).ToArray()));
            if (updated.Revision != 2 || updated.MemberCount != 9_999) throw new InvalidOperationException("A permitted non-creator must be able to edit membership.");
            await RejectAsync(() => UseAsync(repo => repo.UpdateAsync(firstSubject, firstActor, created.Id, 1, "Stale", ids)), AssetViewFailureCodes.StaleRevision);
            await UseAsync(async repo => { await repo.DeleteAsync(secondSubject, secondActor, created.Id, 2); return true; });
            if ((await UseAsync(repo => repo.ListAsync(firstSubject))).Any(view => view.Id == created.Id))
                throw new InvalidOperationException("View deletion failed.");
        }
        finally
        {
            fixture.ChangeTracker.Clear();
            await fixture.AssetViews.Where(view => view.ConnectionId == firstConnection || view.ConnectionId == secondConnection || view.Id == legacyId).ExecuteDeleteAsync();
            await fixture.Assets.Where(asset => asset.OwnerConnectionId == firstConnection || asset.OwnerConnectionId == secondConnection).ExecuteDeleteAsync();
            await fixture.Connections.Where(connection => connection.Id == firstConnection || connection.Id == secondConnection).ExecuteDeleteAsync();
            await fixture.AccessGrants.Where(grant => grant.RoleId == roleId).ExecuteDeleteAsync();
            await fixture.AuditEvents.Where(audit => audit.ActorUserId == firstUser || audit.ActorUserId == secondUser).ExecuteDeleteAsync();
            await fixture.Users.Where(user => user.Id == firstUser || user.Id == secondUser).ExecuteDeleteAsync();
            await fixture.RolePermissions.Where(permission => permission.RoleId == roleId).ExecuteDeleteAsync();
            await fixture.RoleDefinitions.Where(role => role.Id == roleId).ExecuteDeleteAsync();
        }

        async Task<T> UseAsync<T>(Func<AssetViewRepository, Task<T>> action)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            return await action(new AssetViewRepository(db));
        }
    }

    private static async Task RejectAsync(Func<Task> operation, string? code = null)
    {
        try { await operation(); }
        catch (WellSiteAutoPilotException exception) when (code is null || exception.Code == code) { return; }
        throw new InvalidOperationException("Expected View validation or conflict failure.");
    }
}
