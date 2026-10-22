using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Connections.Importing;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Connections;
using WellSiteAutoPilot.Persistence.Security;

internal static class SourceChangePersistenceChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid connectionId, AccessSubject subject, SecurityActorContext actor)
    {
        await using var db = new WellSiteAutoPilotDbContext(options);
        var bindings = await db.SourceBindings.AsNoTracking().Where(b => b.ConnectionId == connectionId).OrderBy(b => b.ExternalId).ToArrayAsync();
        var configId = Guid.NewGuid(); var oldRevision = Guid.NewGuid(); var activeRevision = Guid.NewGuid();
        var exactRoleId = Guid.NewGuid(); var exactGrantId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        try
        {
            db.ConfiguredLogicDefinitions.Add(new ConfiguredLogicEntity { Id = configId, Name = "Source impact fixture", ActiveRevisionId = activeRevision, CreatedAtUtc = now });
            foreach (var (id, number, status) in new[] { (oldRevision, 1, "Superseded"), (activeRevision, 2, "Active") })
            {
                db.ConfiguredLogicRevisions.Add(new ConfiguredLogicRevisionEntity { Id = id, ConfiguredLogicId = configId,
                    RevisionNumber = number, ModuleId = "source-fixture", ModuleVersion = "1.0.0", Mode = "Shadow", Status = status, CreatedAtUtc = now });
                db.ConfiguredLogicAssetBindings.Add(new ConfiguredLogicAssetBindingEntity { RevisionId = id, AssetId = bindings[0].AssetId, Role = "Well" });
            }
            db.ConfiguredLogicDataBindings.Add(new ConfiguredLogicDataBindingEntity { RevisionId = activeRevision, AssetId = bindings[1].AssetId,
                RequirementId = "input-only", ProviderId = "foresite", ProviderAssetExternalId = bindings[1].ExternalId });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var wellIds = bindings.Select(b => b.AssetId).ToArray();
            var originalAssets = await db.Assets.AsNoTracking().Where(a => wellIds.Contains(a.Id)).OrderBy(a => a.Id).Select(a => new { a.Id, a.Name, a.ParentAssetId, a.AttributeValuesJson }).ToArrayAsync();
            var originalViews = await db.AssetViewMembers.AsNoTracking().Where(m => wellIds.Contains(m.AssetId)).OrderBy(m => m.ViewId).ThenBy(m => m.AssetId).ToArrayAsync();
            DiscoveredAsset Well(int index, bool changed)
            {
                var binding = bindings[index]; var accepted = ImportedSourceBaseline.Read(binding.SnapshotJson)!;
                var facts = new Dictionary<string, string>(accepted.Facts);
                if (changed) facts[index == 0 ? "wellType" : "deviceType"] = index == 0 ? "RRL" : "Controller B";
                return new(binding.ExternalId, accepted.SourceName!, binding.ResourceKind, null, facts);
            }
            var changed = new DiscoveryResult(true, [Well(0, true), Well(1, true), Well(2, false)], [], "Gateway account", [], ["0", "101", "202"]);
            var first = await Check(changed);
            CheckTrue(first.Changed == 2 && first.Status == "Complete" && first.SourceScopeCount == 3, "complete check records changes and actual authorized scope");
            var page = await Read();
            CheckTrue(page.Total == 2 && page.Items.Single(c => c.AssetId == bindings[0].AssetId).Configurations.Count == 2 &&
                page.Items.Single(c => c.AssetId == bindings[1].AssetId).Configurations.Single().Kind == "Input", "all pinned revisions and input-only bindings are recorded");
            CheckTrue(page.Items.SelectMany(i => i.Configurations).Any(r => r.Status == "Superseded") && page.Items.SelectMany(i => i.Configurations).Any(r => r.Status == "Active"),
                "active and historical revisions remain distinguishable");
            await Check(changed);
            CheckTrue((await Read()).Total == 2, "identical checks deduplicate durable evidence");
            await Check(new(true, [Well(0, false), Well(1, false), Well(2, false)], [], "Gateway account", [], ["0", "101", "202"]));
            CheckTrue((await Read()).Total == 2, "source reversion cannot clear prior findings");
            await Check(new(false, [], ["Source timed out"], "Gateway account", ["deviceType"]));
            CheckTrue((await Read()).Total == 2 && (await Read()).LatestCheck!.Status == "Partial", "incomplete inventory never invents missing wells or clears findings");
            await Check(null);
            CheckTrue((await Read()).Total == 2 && (await Read()).LatestCheck!.Status == "Failed" && (await Read()).LastSuccessfulCheck is not null,
                "failed check persists and preserves successful freshness and evidence");
            var missing = await Check(new(true, [Well(0, false), Well(2, false)], [], "different account", [], ["0", "101"]));
            CheckTrue(missing.NotReturned == 1 && missing.Warnings.Count >= 2 && (await Read()).Items.Any(i => i.Status == "NotReturned"),
                "missing wells stay not-returned with account and scope-change attention");
            var inventory = Enumerable.Range(0, 10001).Select(i => new DiscoveredAsset("new-" + i, "New " + i, "foresite.well", null,
                new Dictionary<string, string> { ["wellType"] = "ESP" })).Concat(new[] { Well(0, false), Well(1, false), Well(2, false) }).ToArray();
            await Check(new(true, inventory, [], "Gateway account", [], ["0", "101", "202"]));
            var search = await Read(0, 25, "New 10000");
            CheckTrue(search.Total == 1 && search.Items.Single().ExternalId == "new-10000", "full inventory is persisted and searchable beyond initial discovery sample");
            CheckTrue((await Read(1, 1)).Items.Count == 1, "review paging is bounded");
            // Remove current View membership: immutable configuration bindings remain the impact authority.
            await db.AssetViewMembers.Where(m => m.AssetId == bindings[0].AssetId).ExecuteDeleteAsync();
            CheckTrue((await Read(0, 25, "Well 1")).Items.Single().Configurations.Count == 2, "View removal does not lose pinned configuration impacts");
            foreach (var member in originalViews) db.AssetViewMembers.Add(new() { AssetId = member.AssetId, ViewId = member.ViewId });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            CheckTrue((await db.SourceBindings.AsNoTracking().Where(b => b.ConnectionId == connectionId).OrderBy(b => b.ExternalId).ToArrayAsync())
                .Select(b => b.SnapshotJson).SequenceEqual(bindings.Select(b => b.SnapshotJson)) &&
                (await db.Assets.AsNoTracking().Where(a => wellIds.Contains(a.Id)).OrderBy(a => a.Id).Select(a => new { a.Id, a.Name, a.ParentAssetId, a.AttributeValuesJson }).ToArrayAsync()).SequenceEqual(originalAssets),
                "checks leave accepted baselines, names, IDs, attributes and parent paths untouched");
            // Scope the configuration-read grant to the target only. Its input-only dependency must hide the complete definition.
            await db.RolePermissions.Where(p => p.Permission == WellSitePermissions.ConfiguredLogicRead &&
                db.AccessGrants.Any(g => g.RoleId == p.RoleId && g.UserId == subject.UserId)).ExecuteDeleteAsync();
            db.RoleDefinitions.Add(new RoleDefinitionEntity { Id = exactRoleId, Key = exactRoleId.ToString(), Name = "Exact source fixture", NormalizedName = exactRoleId.ToString(),
                IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.RolePermissions.Add(new RolePermissionEntity { RoleId = exactRoleId, Permission = WellSitePermissions.ConfiguredLogicRead });
            db.AccessGrants.Add(new AccessGrantEntity { Id = exactGrantId, UserId = subject.UserId, RoleId = exactRoleId, ScopeKind = "ExactAssets", IsActive = true,
                Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.AccessGrantAssets.Add(new AccessGrantAssetEntity { GrantId = exactGrantId, AssetId = bindings[0].AssetId });
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            CheckTrue((await Read(0, 25, "Well 1")).Items.Single().Configurations.Count == 0, "hidden input scope prevents configuration name and revision disclosure");
            await using (var baselineDb = new WellSiteAutoPilotDbContext(options))
            {
                var repository = new SourceChangeRepository(baselineDb);
                var started = await repository.BeginAsync(connectionId, 1, subject, actor, default);
                await db.SourceBindings.Where(b => b.Id == bindings[0].Id).ExecuteUpdateAsync(set => set.SetProperty(b => b.SnapshotJson, bindings[0].SnapshotJson + " "));
                CheckTrue((await repository.CompleteAsync(connectionId, started.Id, changed, subject, actor, default)).Status == "Stale",
                    "accepted baseline changes during discovery invalidate the result");
                await db.SourceBindings.Where(b => b.Id == bindings[0].Id).ExecuteUpdateAsync(set => set.SetProperty(b => b.SnapshotJson, bindings[0].SnapshotJson));
            }
            // Begin is fenced before the expensive Gateway read; a second checker cannot overlap.
            await using var beginDb = new WellSiteAutoPilotDbContext(options);
            var begun = await new SourceChangeRepository(beginDb).BeginAsync(connectionId, 1, subject, actor, default);
            await Reject(() => Check(changed), "SOURCE_CHECK_IN_PROGRESS");
            await db.Connections.Where(c => c.Id == connectionId).ExecuteUpdateAsync(set => set.SetProperty(c => c.Revision, 2));
            await using var staleDb = new WellSiteAutoPilotDbContext(options);
            var stale = await new SourceChangeRepository(staleDb).CompleteAsync(connectionId, begun.Id, changed, subject, actor, default);
            CheckTrue(stale.Status == "Stale", "changed Connection revision cannot publish source evidence");
            await db.Connections.Where(c => c.Id == connectionId).ExecuteUpdateAsync(set => set.SetProperty(c => c.Revision, 1));
            await db.AccessGrants.Where(g => g.UserId == subject.UserId && g.ScopeKind == "AllAssets")
                .ExecuteUpdateAsync(set => set.SetProperty(g => g.IsActive, false));
            await Reject(() => Read(), "SECURITY_SCOPE_DENIED");
            await Reject(() => Check(changed), "SECURITY_SCOPE_DENIED");
            Console.WriteLine("Durable source check, pinned/input impact, retention, paging and permission checks passed.");
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.SourceChangeImpacts.Where(i => db.SourceChanges.Any(c => c.Id == i.ChangeId && c.ConnectionId == connectionId)).ExecuteDeleteAsync();
            await db.SourceChanges.Where(c => c.ConnectionId == connectionId).ExecuteDeleteAsync();
            await db.SourceChecks.Where(c => c.ConnectionId == connectionId).ExecuteDeleteAsync();
            await db.ConfiguredLogicDataBindings.Where(b => b.RevisionId == oldRevision || b.RevisionId == activeRevision).ExecuteDeleteAsync();
            await db.ConfiguredLogicAssetBindings.Where(b => b.RevisionId == oldRevision || b.RevisionId == activeRevision).ExecuteDeleteAsync();
            await db.ConfiguredLogicRevisions.Where(r => r.ConfiguredLogicId == configId).ExecuteDeleteAsync();
            await db.ConfiguredLogicDefinitions.Where(c => c.Id == configId).ExecuteDeleteAsync();
            await db.AccessGrantAssets.Where(g => g.GrantId == exactGrantId).ExecuteDeleteAsync();
            await db.AccessGrants.Where(g => g.Id == exactGrantId).ExecuteDeleteAsync();
            await db.RolePermissions.Where(p => p.RoleId == exactRoleId).ExecuteDeleteAsync();
            await db.RoleDefinitions.Where(r => r.Id == exactRoleId).ExecuteDeleteAsync();
        }
        async Task<SourceCheckSummary> Check(DiscoveryResult? result)
        {
            await using var context = new WellSiteAutoPilotDbContext(options);
            var repository = new SourceChangeRepository(context);
            var start = await repository.BeginAsync(connectionId, 1, subject, actor, default);
            return await repository.CompleteAsync(connectionId, start.Id, result, subject, actor, default);
        }
        async Task<SourceChangePage> Read(int offset = 0, int limit = 25, string? query = null)
        {
            await using var context = new WellSiteAutoPilotDbContext(options);
            return await new SourceChangeRepository(context).ReadAsync(connectionId, offset, limit, query, subject, default);
        }
    }
    private static async Task Reject(Func<Task> action, string code)
    {
        try { await action(); throw new InvalidOperationException("Expected rejection: " + code); }
        catch (WellSiteAutoPilotException error) when (error.Code == code) { }
    }
    private static void CheckTrue(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Source change persistence failed: " + message); }
}
