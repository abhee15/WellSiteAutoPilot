using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Connections.Importing;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Assets.Views;
using WellSiteAutoPilot.Persistence.Connections;
using WellSiteAutoPilot.Persistence.Security;

internal static class HierarchyPersistenceChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid wellTypeId)
    {
        var now = DateTimeOffset.UtcNow; var userId = Guid.NewGuid(); var roleId = Guid.NewGuid(); var connectionId = Guid.NewGuid();
        var firstRaceConnectionId = Guid.NewGuid(); var ownedConnections = new[] { connectionId, firstRaceConnectionId };
        var subject = new AccessSubject(userId); var actor = new SecurityActorContext(userId, "hierarchy-fixture", "hierarchy-persistence");
        await using var fixture = new WellSiteAutoPilotDbContext(options);
        var groupType = await fixture.AssetTypes.FirstOrDefaultAsync(type => type.Key == "group");
        var createdType = groupType is null;
        groupType ??= new AssetTypeEntity { Id = Guid.NewGuid(), Key = "group", DisplayName = "Group", SchemaVersion = 1,
            AttributeSchemaJson = "{\"type\":\"object\"}", IsActive = true, CreatedAtUtc = now };
        try
        {
            if (createdType) fixture.AssetTypes.Add(groupType);
            fixture.Users.Add(new UserEntity { Id = userId, IdentityKey = userId.ToString(), IdentityName = actor.IdentityName,
                NormalizedIdentityName = actor.IdentityName.ToUpperInvariant(), IsActive = true, CreatedAtUtc = now, LastSeenAtUtc = now });
            fixture.RoleDefinitions.Add(new RoleDefinitionEntity { Id = roleId, Key = roleId.ToString(), Name = roleId.ToString(),
                NormalizedName = roleId.ToString().ToUpperInvariant(), IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            foreach (var permission in new[] { WellSitePermissions.AssetsManage, WellSitePermissions.ConnectionsManage })
                fixture.RolePermissions.Add(new RolePermissionEntity { RoleId = roleId, Permission = permission });
            foreach (var scope in new[] { "AllAssets", "DeploymentWide" }) fixture.AccessGrants.Add(new AccessGrantEntity {
                Id = Guid.NewGuid(), UserId = userId, RoleId = roleId, ScopeKind = scope, IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            foreach (var owner in ownedConnections) fixture.Connections.Add(new ConnectionEntity { Id = owner, Name = owner.ToString(), NormalizedName = owner.ToString(),
                AdapterKey = "foresite", SettingsVersion = 1, IsEnabled = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            await fixture.SaveChangesAsync(); fixture.ChangeTracker.Clear();
            DiscoveryField[] fields = [new("assetName", "Asset", "Unassigned", false, "assetId")];
            string[] levels = ["assetName"];
            DiscoveredAsset Well(string id, string source) => new(id, "Well " + id, "foresite.well", null,
                new Dictionary<string, string> { ["assetName"] = "Repeated asset name", ["assetId"] = source });
            var first = ImportPlanner.Plan(connectionId, [Well("1", "101"), Well("2", "202")], levels, fields);
            var emptyState = await new ConnectionImportRepository(fixture).ReadHierarchyAsync(connectionId);
            await Apply(first, emptyState.Fingerprint, false);
            var saved = await new ConnectionImportRepository(fixture).ReadHierarchyAsync(connectionId);
            Check(saved.Status.Status == "Saved" && saved.Levels!.SequenceEqual(levels), "first import atomically saves the canonical recipe");
            var bindings = await fixture.SourceBindings.AsNoTracking().Where(binding => binding.ConnectionId == connectionId).OrderBy(binding => binding.ExternalId).ToArrayAsync();
            var parents = await fixture.Assets.AsNoTracking().Where(asset => asset.OwnerConnectionId == connectionId).OrderBy(asset => asset.Id).ToArrayAsync();
            Check(bindings.Length == 2 && parents.Length == 4 && bindings.Select(binding => ImportedSourceBaseline.Read(binding.SnapshotJson)!.SourceName).SequenceEqual(["Well 1", "Well 2"]),
                "same-named groups stay distinct and original source names round-trip");
            var changedRecipe = ImportPlanner.Plan(connectionId, [Well("3", "303")], [], fields);
            await Reject(() => Apply(changedRecipe, saved.Fingerprint, false), "another recipe is refused inside the write");
            Check(await fixture.Assets.CountAsync(asset => asset.OwnerConnectionId == connectionId) == 4, "refusal creates no partial Assets");

            var competingPlans = new[] {
                ImportPlanner.Plan(firstRaceConnectionId, [Well("first", "101")], levels, fields),
                ImportPlanner.Plan(firstRaceConnectionId, [Well("first", "101")], [], fields) };
            var firstRaceState = await new ConnectionImportRepository(fixture).ReadHierarchyAsync(firstRaceConnectionId);
            var firstResults = await Task.WhenAll(competingPlans.Select(async candidate =>
            {
                try { await Apply(candidate, firstRaceState.Fingerprint, false, firstRaceConnectionId); return true; }
                catch (WellSiteAutoPilotException error) when (error.Code is "CONNECTION_IMPORT_PLAN_CHANGED" or "SECURITY_ACCESS_CONFLICT") { return false; }
            }));
            var winningRecipe = await new ConnectionImportRepository(fixture).ReadHierarchyAsync(firstRaceConnectionId);
            Check(firstResults.Count(result => result) == 1 && winningRecipe.Status.Status == "Saved" &&
                await fixture.SourceBindings.CountAsync(binding => binding.ConnectionId == firstRaceConnectionId) == 1 &&
                await fixture.Assets.CountAsync(asset => asset.OwnerConnectionId == firstRaceConnectionId) == winningRecipe.Levels!.Count + 1,
                "competing first imports establish one recipe without orphan Assets from the losing plan");

            // Simulate a pre-recipe installation while retaining its actual Assets, source facts and View membership.
            var viewId = Guid.NewGuid();
            fixture.AssetViews.Add(new AssetViewEntity { Id = viewId, ConnectionId = connectionId, OwnerUserId = userId,
                Name = "Pinned fixture", NormalizedName = "PINNED FIXTURE", DefinitionJson = "{}", Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            fixture.AssetViewMembers.Add(new AssetViewMemberEntity { ViewId = viewId, AssetId = bindings[0].AssetId });
            await fixture.SaveChangesAsync(); fixture.ChangeTracker.Clear();
            await fixture.ImportedGroups.Where(item => item.ConnectionId == connectionId).ExecuteDeleteAsync();
            await fixture.ImportHierarchies.Where(item => item.ConnectionId == connectionId).ExecuteDeleteAsync();
            var legacy = await new ConnectionImportRepository(fixture).ReadHierarchyAsync(connectionId);
            Check(legacy.Status.Status == "Legacy", "existing imports require explicit reviewed adoption");
            var noNewWells = ImportPlanner.Plan(connectionId, [], levels, fields);
            await Reject(() => Apply(noNewWells, legacy.Fingerprint, false), "adoption cannot happen without consent");
            var root = parents.First(asset => asset.ParentAssetId is null);
            await fixture.Assets.Where(asset => asset.Id == root.Id).ExecuteUpdateAsync(update => update.SetProperty(asset => asset.Name, "Concurrent local rename"));
            await Reject(() => Apply(noNewWells, legacy.Fingerprint, true), "a stale adoption cannot commit after a local edit");
            Check(!await fixture.ImportHierarchies.AnyAsync(item => item.ConnectionId == connectionId), "stale adoption rolls back the recipe");
            await fixture.Assets.Where(asset => asset.Id == root.Id).ExecuteUpdateAsync(update => update.SetProperty(asset => asset.Name, root.Name));
            legacy = await new ConnectionImportRepository(fixture).ReadHierarchyAsync(connectionId);
            var adopted = await Apply(noNewWells, legacy.Fingerprint, true);
            Check(adopted.HierarchyAdopted && adopted.WellsCreated == 0 && adopted.GroupsCreated == 0, "adoption works without importing replacement wells");
            var after = await fixture.Assets.AsNoTracking().Where(asset => asset.OwnerConnectionId == connectionId).OrderBy(asset => asset.Id).ToArrayAsync();
            Check(after.Select(asset => (asset.Id, asset.Name, asset.ParentAssetId)).SequenceEqual(parents.Select(asset => (asset.Id, asset.Name, asset.ParentAssetId))) &&
                await fixture.AssetViewMembers.AnyAsync(member => member.ViewId == viewId && member.AssetId == bindings[0].AssetId),
                "legacy adoption preserves IDs, local paths and View membership");
            Check((await fixture.SourceBindings.AsNoTracking().Where(binding => binding.ConnectionId == connectionId).OrderBy(binding => binding.ExternalId).ToArrayAsync())
                .Select(binding => (binding.Id, binding.AssetId, binding.SnapshotJson)).SequenceEqual(bindings.Select(binding => (binding.Id, binding.AssetId, binding.SnapshotJson))),
                "adoption never silently accepts new source facts");
            var reusedRoot = parents.Single(asset => asset.Id == bindings[0].AssetId).ParentAssetId!.Value;
            await fixture.Assets.Where(asset => asset.Id == reusedRoot).ExecuteUpdateAsync(update => update.SetProperty(asset => asset.Name, "Kept local label"));
            saved = await new ConnectionImportRepository(fixture).ReadHierarchyAsync(connectionId);
            var newWell = ImportPlanner.Plan(connectionId, [Well("3", "101")], levels, fields, saved.StableGroups!.ToDictionary(group => group.Key, group => group.Name));
            var outcomes = await Task.WhenAll(Race(), Race());
            Check(outcomes.Count(result => result) == 1 && await fixture.SourceBindings.CountAsync(binding => binding.ConnectionId == connectionId) == 3,
                "concurrent imports create one well and retain one canonical recipe");
            Check(await fixture.Assets.AnyAsync(asset => asset.Id == reusedRoot && asset.Name == "Kept local label") &&
                await fixture.ImportedGroups.AnyAsync(group => group.AssetId == reusedRoot && group.SourceLabel == "Repeated asset name"),
                "new imports preserve local group labels and their independent accepted source label");
            async Task<bool> Race()
            {
                try { await Apply(newWell, saved.Fingerprint, false); return true; }
                catch (WellSiteAutoPilotException error) when (error.Code is "CONNECTION_IMPORT_PLAN_CHANGED" or "SECURITY_ACCESS_CONFLICT") { return false; }
            }
            async Task<ImportApplyResult> Apply(ImportPlan plan, string fingerprint, bool adopt, Guid? owner = null)
            {
                await using var db = new WellSiteAutoPilotDbContext(options);
                return await new ConnectionImportRepository(db).ApplyAuthorizedAsync(new ImportApplyRequest(owner ?? connectionId, plan,
                    wellTypeId, groupType.Id, 1, fingerprint, adopt, fields, [], actor.IdentityName), subject, actor);
            }
        }
        finally
        {
            fixture.ChangeTracker.Clear();
            await fixture.AuditEvents.Where(item => item.ActorUserId == userId).ExecuteDeleteAsync();
            await fixture.AssetViewMembers.Where(member => fixture.AssetViews.Any(view => view.Id == member.ViewId && view.ConnectionId != null && ownedConnections.Contains(view.ConnectionId.Value))).ExecuteDeleteAsync();
            await fixture.AssetViews.Where(view => view.ConnectionId != null && ownedConnections.Contains(view.ConnectionId.Value)).ExecuteDeleteAsync();
            await fixture.SourceBindings.Where(binding => ownedConnections.Contains(binding.ConnectionId)).ExecuteDeleteAsync();
            await fixture.ImportedGroups.Where(item => ownedConnections.Contains(item.ConnectionId)).ExecuteDeleteAsync();
            await fixture.ImportHierarchies.Where(item => ownedConnections.Contains(item.ConnectionId)).ExecuteDeleteAsync();
            await fixture.Assets.Where(asset => asset.OwnerConnectionId != null && ownedConnections.Contains(asset.OwnerConnectionId.Value)).ExecuteDeleteAsync();
            await fixture.Connections.Where(item => ownedConnections.Contains(item.Id)).ExecuteDeleteAsync();
            await fixture.AccessGrants.Where(item => item.UserId == userId).ExecuteDeleteAsync();
            await fixture.RolePermissions.Where(item => item.RoleId == roleId).ExecuteDeleteAsync();
            await fixture.RoleDefinitions.Where(item => item.Id == roleId).ExecuteDeleteAsync();
            await fixture.Users.Where(item => item.Id == userId).ExecuteDeleteAsync();
            if (createdType) await fixture.AssetTypes.Where(type => type.Id == groupType.Id).ExecuteDeleteAsync();
        }
    }
    private static async Task Reject(Func<Task<ImportApplyResult>> action, string message)
    {
        try { await action(); throw new InvalidOperationException(message); }
        catch (WellSiteAutoPilotException error) when (error.Code == "CONNECTION_IMPORT_PLAN_CHANGED") { }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Hierarchy persistence check failed: " + message);
    }
}
