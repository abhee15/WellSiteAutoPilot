using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets.Views;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Logic;
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
                WellSitePermissions.AssetViewsCreate, WellSitePermissions.AssetViewsEdit, WellSitePermissions.AssetViewsDelete,
                WellSitePermissions.ConfiguredLogicRead, WellSitePermissions.ConfiguredLogicManage })
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
            var deleteRevision = await VerifyAssignmentAsync(created.Id, firstConnection, foreign.Id, ids, firstSubject, firstActor);
            // A broad authorized editor can remove an inactive well; a View must not become permanently uneditable.
            var inactiveWellId = wells[^1].Id;
            await fixture.Assets.Where(asset => asset.Id == inactiveWellId).ExecuteUpdateAsync(set => set.SetProperty(asset => asset.IsActive, false));
            var unavailableView = await UseAsync(repo => repo.FindAsync(firstSubject, created.Id)) ?? throw new InvalidOperationException("Missing View.");
            var maintenance = await UseAsync(repo => repo.SelectionAsync(firstSubject, created.Id));
            if (!unavailableView.HasUnavailableMembers || !unavailableView.CanInspectAllMembers ||
                maintenance.UnavailableMembers.Count != 1 || maintenance.UnavailableMembers[0].Id != wells[^1].Id)
                throw new InvalidOperationException("Inactive members must be identifiable to an authorized editor for removal.");
            var repaired = await UseAsync(repo => repo.UpdateAsync(firstSubject, firstActor, created.Id, deleteRevision, "Repaired selection", ids.Take(ids.Length - 1).ToArray()));
            deleteRevision = repaired.Revision;
            if (repaired.HasUnavailableMembers) throw new InvalidOperationException("Removing inactive membership did not repair the View.");
            await UseAsync(async repo => { await repo.DeleteAsync(secondSubject, secondActor, created.Id, deleteRevision); return true; });
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

        async Task<int> VerifyAssignmentAsync(Guid viewId, Guid connectionId, Guid foreignId, Guid[] originalIds,
            AccessSubject subject, SecurityActorContext actor)
        {
            var moduleId = "weatherford.view-fixture-" + Guid.NewGuid().ToString("N");
            var manifest = $$"""
                { "manifestVersion":1,"moduleId":"{{moduleId}}","version":"1.0.0","displayName":"View fixture",
                  "publisher":"Weatherford","runtime":"DotNet","executionProfile":"ScheduledOneShot",
                  "entryPoint":"ViewFixture.EntryPoint","minimumSdkVersion":"1.0.0","requiredAdapterKey":"foresite",
                  "assetRequirements":[{"role":"well","minimumCount":1,"maximumCount":10000,"requiredAssetTypeKey":"well","requiredTraits":[]},
                    {"role":"reference","minimumCount":0,"maximumCount":1,"requiredAssetTypeKey":"well","requiredTraits":[]}],
                  "dataRequirements":[],"commandRequirements":[] }
                """;
            Guid? configurationId = null;
            var extraConfigurations = new List<Guid>();
            await using var db = new WellSiteAutoPilotDbContext(options);
            try
            {
                var modules = new LogicModuleCatalogService(new LogicModuleCatalogRepository(db), TimeProvider.System);
                await modules.RegisterAsync(new RegisterLogicModuleCommand(manifest, new string('a', 64), LogicModuleTrustStatus.TrustedPublisher));
                var service = new ConfiguredLogicService(new ConfiguredLogicRepository(db), new AssetRepository(db),
                    new LogicModuleCatalogRepository(db), TimeProvider.System, new AssetViewRepository(db), new ConnectionRepository(db));
                var command = new CreateConfiguredLogicCommand("View fixture assignment", manifest, "{}", [], [], null,
                    new ConfiguredLogicViewBindingCommand(viewId, 2, "well"));
                await RejectAsync(() => service.CreateAuthorizedAsync(command with { AssetBindings = null! }, subject, actor), FailureCodes.ValidationFailed);
                var configured = await service.CreateAuthorizedAsync(command, subject, actor);
                configurationId = configured.Id;
                var revision = configured.Revisions.Single();
                if (revision.View?.ViewId != viewId || revision.View.Revision != 2 || revision.View.ConnectionId != connectionId ||
                    revision.AssetBindings.Count != 9_999) throw new InvalidOperationException("The complete View snapshot was not bound to the configuration.");
                db.ChangeTracker.Clear();
                var changed = await UseAsync(repo => repo.UpdateAsync(subject, actor, viewId, 2, "After assignment", originalIds));
                var persisted = await new ConfiguredLogicRepository(db).GetAsync(configured.Id) ?? throw new InvalidOperationException("Missing configuration.");
                if (persisted.Revisions.Single().AssetBindings.Count != 9_999 || persisted.Revisions.Single().View?.Revision != 2)
                    throw new InvalidOperationException("Editing a View changed a saved configuration snapshot.");
                await RejectAsync(() => service.CreateAuthorizedAsync(command, subject, actor), "ASSET_VIEW_REVISION_STALE");
                await RejectAsync(() => UseAsync(async repo => { await repo.DeleteAsync(subject, actor, viewId, changed.Revision); return true; }), "ASSET_VIEW_REFERENCED");
                // All roles remain in the same Connection, not just the role populated from the View.
                await RejectAsync(() => service.CreateAuthorizedAsync(command with { Name = "Foreign reference", View = command.View! with { ExpectedRevision = 3 },
                    AssetBindings = [new ConfiguredLogicAssetBindingCommand("reference", foreignId, "{}")] }, subject, actor), "CONFIGURED_LOGIC_ASSET_NOT_FOUND");
                var wrongAdapter = manifest.Replace(moduleId, moduleId + "-wrong", StringComparison.Ordinal)
                    .Replace("\"requiredAdapterKey\":\"foresite\"", "\"requiredAdapterKey\":\"petrovisor\"", StringComparison.Ordinal);
                await modules.RegisterAsync(new RegisterLogicModuleCommand(wrongAdapter, new string('b', 64), LogicModuleTrustStatus.TrustedPublisher));
                await RejectAsync(() => service.CreateAuthorizedAsync(command with { Name = "Wrong adapter", ModuleManifestJson = wrongAdapter,
                    View = command.View! with { ExpectedRevision = 3 } }, subject, actor), "CONFIGURED_LOGIC_VIEW_INCOMPATIBLE");
                var single = manifest.Replace(moduleId, moduleId + "-single", StringComparison.Ordinal)
                    .Replace("\"maximumCount\":10000", "\"maximumCount\":1", StringComparison.Ordinal);
                await modules.RegisterAsync(new RegisterLogicModuleCommand(single, new string('c', 64), LogicModuleTrustStatus.TrustedPublisher));
                await RejectAsync(() => service.CreateAuthorizedAsync(command with { Name = "Single-well mismatch", ModuleManifestJson = single,
                    View = command.View! with { ExpectedRevision = 3 } }, subject, actor), "CONFIGURED_LOGIC_ASSET_CARDINALITY_INVALID");
                // Adapter routing and stored source identity are authoritative for View data inputs.
                var sourceView = await UseAsync(repo => repo.CreateAsync(subject, actor, connectionId, "Source identity fixture", [originalIds[0]]));
                db.SourceBindings.Add(new SourceBindingEntity { Id = Guid.NewGuid(), ConnectionId = connectionId, AssetId = originalIds[0],
                    ResourceKind = "foresite.well", ExternalId = "FS-VIEW-0001", CreatedAtUtc = DateTimeOffset.UtcNow, SnapshotJson = "{}" });
                await db.SaveChangesAsync();
                var dataManifest = manifest.Replace(moduleId, moduleId + "-data", StringComparison.Ordinal)
                    .Replace("\"dataRequirements\":[]", "\"dataRequirements\":[{\"id\":\"value\",\"assetRole\":\"well\",\"quantity\":\"PumpFillage\",\"access\":\"Current\",\"allowUncertainQuality\":false}]", StringComparison.Ordinal);
                await modules.RegisterAsync(new RegisterLogicModuleCommand(dataManifest, new string('d', 64), LogicModuleTrustStatus.TrustedPublisher));
                var dataCommand = command with { Name = "Source fixture", ModuleManifestJson = dataManifest,
                    View = new ConfiguredLogicViewBindingCommand(sourceView.Id, 1, "well"),
                    DataBindings = [new ConfiguredLogicDataBindingCommand("value", originalIds[0], "simulator", "FS-VIEW-0001", "{}")] };
                await RejectAsync(() => service.CreateAuthorizedAsync(dataCommand, subject, actor), "CONFIGURED_LOGIC_VIEW_SOURCE_MISMATCH");
                await RejectAsync(() => service.CreateAuthorizedAsync(dataCommand with { DataBindings =
                    [new ConfiguredLogicDataBindingCommand("value", originalIds[0], "foresite", "OTHER-SOURCE", "{}")] }, subject, actor), "CONFIGURED_LOGIC_VIEW_SOURCE_MISMATCH");
                var validSource = await service.CreateAuthorizedAsync(dataCommand with { DataBindings =
                    [new ConfiguredLogicDataBindingCommand("value", originalIds[0], "foresite", "FS-VIEW-0001", "{}")] }, subject, actor);
                extraConfigurations.Add(validSource.Id);
                await db.Connections.Where(connection => connection.Id == connectionId).ExecuteUpdateAsync(set => set.SetProperty(connection => connection.IsEnabled, false));
                await RejectAsync(() => service.CreateAuthorizedAsync(command with { Name = "Disabled Connection",
                    View = command.View! with { ExpectedRevision = 3 } }, subject, actor), "CONFIGURED_LOGIC_VIEW_INCOMPATIBLE");
                await db.Connections.Where(connection => connection.Id == connectionId).ExecuteUpdateAsync(set => set.SetProperty(connection => connection.IsEnabled, true));
                // Lifecycle operations retain the reviewed snapshot after the source View has changed.
                await service.ValidateRevisionAuthorizedAsync(configured.Id, revision.Id, subject, actor);
                await db.Connections.Where(connection => connection.Id == connectionId).ExecuteUpdateAsync(set => set.SetProperty(connection => connection.IsEnabled, false));
                await RejectAsync(() => service.ActivateRevisionAuthorizedAsync(configured.Id, revision.Id, "none", subject, actor), "CONFIGURED_LOGIC_REVISION_CONFLICT");
                await db.Connections.Where(connection => connection.Id == connectionId).ExecuteUpdateAsync(set => set.SetProperty(connection => connection.IsEnabled, true));
                var active = await service.ActivateRevisionAuthorizedAsync(configured.Id, revision.Id, "none", subject, actor);
                var run = await service.PrepareManualShadowAsync(configured.Id, active.Id);
                if (run.Assets.Count != 9_999 || active.View?.Revision != 2)
                    throw new InvalidOperationException("Lifecycle or execution preparation silently refreshed View membership.");
                return changed.Revision;
            }
            finally
            {
                db.ChangeTracker.Clear();
                await db.ConfiguredLogicDefinitions.Where(item => item.Id == configurationId || extraConfigurations.Contains(item.Id)).ExecuteDeleteAsync();
                await db.SourceBindings.Where(binding => binding.ConnectionId == connectionId && binding.ExternalId == "FS-VIEW-0001").ExecuteDeleteAsync();
                await db.LogicModules.Where(module => module.ModuleId == moduleId || module.ModuleId == moduleId + "-wrong" ||
                    module.ModuleId == moduleId + "-single" || module.ModuleId == moduleId + "-data").ExecuteDeleteAsync();
                await db.Connections.Where(connection => connection.Id == connectionId).ExecuteUpdateAsync(set => set.SetProperty(connection => connection.IsEnabled, true));
            }
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
