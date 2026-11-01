using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Security;

internal static class AssetDetailReadChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid typeId)
    {
        await using var db = new WellSiteAutoPilotDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var user = Guid.NewGuid(); var role = Guid.NewGuid(); var grant = Guid.NewGuid();
        var assetA = Guid.NewGuid(); var assetB = Guid.NewGuid(); var hiddenAsset = Guid.NewGuid();
        var name = "asset-details-" + user.ToString("N");
        db.Users.Add(new() { Id = user, IdentityKey = name, IdentityName = name, NormalizedIdentityName = name,
            IsActive = true, CreatedAtUtc = now, LastSeenAtUtc = now });
        db.RoleDefinitions.Add(new() { Id = role, Key = name, Name = name, NormalizedName = name,
            IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
        foreach (var permission in new[] { WellSitePermissions.AssetsRead, WellSitePermissions.ConfiguredLogicRead, WellSitePermissions.ExecutionsRead })
            db.RolePermissions.Add(new() { RoleId = role, Permission = permission });
        db.AccessGrants.Add(new() { Id = grant, UserId = user, SubjectKind = "User", RoleId = role, ScopeKind = "ExactAssets",
            IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
        foreach (var id in new[] { assetA, assetB, hiddenAsset })
            db.Assets.Add(new() { Id = id, AssetTypeId = typeId, Name = name, IsActive = true, CreatedAtUtc = now,
                ParentAssetId = id == assetB ? assetA : null });
        foreach (var id in new[] { assetA, assetB }) db.AccessGrantAssets.Add(new() { GrantId = grant, AssetId = id });
        var target = new List<(Guid Id, Guid Revision)>();
        for (var i = 0; i < 12; i++)
        {
            var id = Guid.NewGuid(); var revision = Guid.NewGuid();
            db.ConfiguredLogicDefinitions.Add(new() { Id = id, Name = (i < 5 ? "A" : "Z") + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), CreatedAtUtc = now });
            db.ConfiguredLogicRevisions.Add(new() { Id = revision, ConfiguredLogicId = id, RevisionNumber = 1,
                ModuleId = "asset-details", ModuleVersion = "1.0.0", Mode = "Shadow", Status = "Draft", CreatedAtUtc = now });
            db.ConfiguredLogicAssetBindings.Add(new() { RevisionId = revision, Role = "target", AssetId = i < 5 ? assetB : assetA });
            if (i >= 5) target.Add((id, revision));
        }
        // An otherwise matching configuration spanning an unauthorized Asset must be excluded entirely.
        var hiddenConfig = Guid.NewGuid(); var hiddenRevision = Guid.NewGuid();
        db.ConfiguredLogicDefinitions.Add(new() { Id = hiddenConfig, Name = "A-hidden", CreatedAtUtc = now });
        db.ConfiguredLogicRevisions.Add(new() { Id = hiddenRevision, ConfiguredLogicId = hiddenConfig, RevisionNumber = 1,
            ModuleId = "asset-details", ModuleVersion = "1.0.0", Mode = "Shadow", Status = "Draft", CreatedAtUtc = now });
        foreach (var id in new[] { assetA, hiddenAsset })
            db.ConfiguredLogicAssetBindings.Add(new() { RevisionId = hiddenRevision, Role = id.ToString(), AssetId = id });
        var groupType = await db.AssetTypes.SingleOrDefaultAsync(type => type.Key == "group");
        if (groupType is null)
        {
            groupType = new() { Id = Guid.NewGuid(), Key = "group", DisplayName = "Group", SchemaVersion = 1, IsActive = true, CreatedAtUtc = now };
            db.AssetTypes.Add(groupType);
        }
        var groupId = Guid.NewGuid();
        db.Assets.Add(new() { Id = groupId, AssetTypeId = groupType.Id, Name = name, IsActive = true, CreatedAtUtc = now });
        db.AccessGrantAssets.Add(new() { GrantId = grant, AssetId = groupId });
        var runs = new List<Guid>();
        for (var i = 1; i <= 6; i++)
        {
            var id = Guid.NewGuid(); runs.Add(id);
            var configuration = i == 6 ? target[1] : target[0];
            db.Executions.Add(new() { Id = id, LogicInstanceId = configuration.Id, ConfigurationRevisionId = configuration.Revision,
                ModuleId = "asset-details", ModuleVersion = "1.0.0", Mode = "Shadow", Status = "Completed",
                CorrelationId = name + i, Trigger = "Manual", RequestedAtUtc = now });
            // Same timestamp exercises the UUID tie-breaker. Run 4 belongs to another Asset;
            // run 5 has a pinned multi-Asset scope the subject cannot fully read.
            db.ExecutionAssetScopes.Add(new() { ExecutionId = id, LogicInstanceId = configuration.Id, AssetId = i == 4 ? assetB : assetA });
            if (i == 5) db.ExecutionAssetScopes.Add(new() { ExecutionId = id, LogicInstanceId = configuration.Id, AssetId = hiddenAsset });
        }
        await db.SaveChangesAsync();
        var subject = new AccessSubject(user);
        var reader = new AssetDetailReader(db, new ConfiguredLogicRepository(db), TimeProvider.System);
        if (await reader.IsGroupingNodeAuthorizedAsync(subject, groupId, default) != true ||
            await reader.IsGroupingNodeAuthorizedAsync(subject, assetA, default) != false ||
            await reader.IsGroupingNodeAuthorizedAsync(subject, hiddenAsset, default) is not null)
            throw new InvalidOperationException("Grouping classification inferred children or disclosed an unauthorized Asset.");
        var first = await reader.ListLogicAuthorizedAsync(subject, assetA, 0, 5, default);
        var second = await reader.ListLogicAuthorizedAsync(subject, assetA, 5, 5, default);
        if (first.Count != 5 || second.Count != 2 || first.Concat(second).Any(item => !target.Any(t => t.Id == item.Id)))
            throw new InvalidOperationException("Asset/configuration filtering did not precede paging or crossed scope.");
        var expected = (await new ExecutionRepository(db).ListAuthorizedAsync(subject, null, 50, target[0].Id, assetA)).Select(item => item.Id).ToArray();
        var page1 = await reader.ListHistoryAuthorizedAsync(subject, assetA, target[0].Id, null, 2, default);
        var page2 = await reader.ListHistoryAuthorizedAsync(subject, assetA, target[0].Id, page1[^1].Id, 2, default);
        if (expected.Length != 3 || !page1.Concat(page2).Select(item => item.Id).SequenceEqual(expected))
            throw new InvalidOperationException("History cursor ordering or scoped/unpaged query parity failed.");
        if ((await reader.ListHistoryAuthorizedAsync(subject, assetA, target[0].Id, runs[3], 2, default)).Count != 0 ||
            (await reader.ListHistoryAuthorizedAsync(subject, assetA, target[0].Id, runs[4], 2, default)).Count != 0)
            throw new InvalidOperationException("History accepted an out-of-filter or inaccessible cursor.");
        await db.AccessGrants.Where(item => item.Id == grant).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
        if ((await reader.ListLogicAuthorizedAsync(subject, assetA, 0, 5, default)).Count != 0 ||
            (await reader.ListHistoryAuthorizedAsync(subject, assetA, target[0].Id, null, 2, default)).Count != 0)
            throw new InvalidOperationException("Asset detail reads retained revoked access.");
        Console.WriteLine("Asset details: explicit grouping, filters before paging, scoped history parity, cursor ties and revoked access verified.");
    }
}
