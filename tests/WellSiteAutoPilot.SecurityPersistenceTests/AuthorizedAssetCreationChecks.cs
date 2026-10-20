using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Assets;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Security;

internal static class AuthorizedAssetCreationChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid administrator)
    {
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var key = "asset-grant-" + Guid.NewGuid().ToString("N");
        var adminSubject = new AccessSubject(administrator);
        var adminActor = new SecurityActorContext(administrator, "bootstrap-test-administrator", key);
        var service = new AssetService(new AssetRepository(setup), TimeProvider.System);
        const string schema = """{"type":"object","properties":{"rpm":{"type":"integer"}},"required":["rpm"],"additionalProperties":false}""";
        var type = await service.CreateAssetTypeAuthorizedAsync(new(key, "Granted type", schema), adminSubject, adminActor);
        var parentA = await service.CreateAssetAuthorizedAsync(new(type.Id, "Parent A", null, """{"rpm":1}"""), adminSubject, adminActor);
        var parentB = await service.CreateAssetAuthorizedAsync(new(type.Id, "Parent B", null, """{"rpm":1}"""), adminSubject, adminActor);
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var readRoleId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var readGrantId = Guid.NewGuid();
        setup.Users.Add(new UserEntity
        {
            Id = userId, IdentityKey = key, IdentityName = key, NormalizedIdentityName = key,
            IsActive = true, CreatedAtUtc = now, LastSeenAtUtc = now
        });
        foreach (var (id, permission) in new[] { (roleId, WellSitePermissions.AssetsManage), (readRoleId, WellSitePermissions.AssetsRead) })
        {
            setup.RoleDefinitions.Add(new RoleDefinitionEntity
            {
                Id = id, Key = id.ToString("N"), Name = id.ToString("N"), NormalizedName = id.ToString("N"),
                IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            });
            setup.RolePermissions.Add(new RolePermissionEntity { RoleId = id, Permission = permission });
        }
        setup.AccessGrants.AddRange(new AccessGrantEntity
        {
            Id = grantId, UserId = userId, RoleId = roleId, ScopeKind = "ExactAssets", IsActive = true,
            Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
        }, new AccessGrantEntity
        {
            Id = readGrantId, SubjectKind = "WindowsGroup", GroupSid = "S-1-5-21-916-1", RoleId = readRoleId,
            ScopeKind = "ExactAssets", IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
        });
        setup.AccessGrantAssets.AddRange(new AccessGrantAssetEntity { GrantId = grantId, AssetId = parentA.Id },
            new AccessGrantAssetEntity { GrantId = readGrantId, AssetId = parentB.Id });
        await setup.SaveChangesAsync();
        var subject = new AccessSubject(userId, new(userId, ["S-1-5-21-916-1"], now));
        var actor = new SecurityActorContext(userId, key, key);
        await ExpectFailureAsync(() => CreateAsync(parentB.Id), FailureKind.Authorization);
        await ExpectFailureAsync(() => CreateAsync(null), FailureKind.Authorization);
        await ExpectFailureAsync(async () =>
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            await new AssetService(new AssetRepository(db), TimeProvider.System)
                .CreateAssetTypeAuthorizedAsync(new(key + "-denied", "Denied type", schema), subject, actor);
        }, FailureKind.Authorization);
        if (await setup.Assets.CountAsync() != 2 || await setup.AssetTypes.CountAsync() != 1 ||
            await setup.AuditEvents.AnyAsync(audit => audit.ActorUserId == userId))
            throw new InvalidOperationException("Denied Asset creation left data or audit state.");
        var child = await CreateAsync(parentA.Id);
        var childAudit = await setup.AuditEvents.SingleAsync(audit => audit.TargetId == child.Id.ToString());
        if (child.ParentAssetId != parentA.Id || childAudit.ActorUserId != userId || childAudit.CorrelationId != key ||
            await new RequestAccessRepository(setup, TimeProvider.System).HasAssetPermissionAsync(subject, WellSitePermissions.AssetsManage, [child.Id]) ||
            await new AssetRepository(setup).GetAuthorizedAsync(subject, child.Id) is not null)
            throw new InvalidOperationException("Child creation lost its audit or silently inherited exact parent access.");

        // A direct repository caller must validate against the current locked
        // schema, rather than trusting earlier application validation.
        await setup.AssetTypes.Where(item => item.Id == type.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.AttributeSchemaJson, """{"type":"object","properties":{"rpm":{"type":"string"}},"required":["rpm"]}"""));
        await ExpectFailureAsync(async () =>
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            await new AssetRepository(db).AddAssetAuthorizedAsync(NewAsset(parentA.Id), subject, actor);
        }, FailureKind.Validation);
        await setup.AssetTypes.Where(item => item.Id == type.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.AttributeSchemaJson, schema));
        var rollback = NewAsset(parentA.Id);
        var failureOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(new RejectAudit()).Options;
        await using (var failing = new WellSiteAutoPilotDbContext(failureOptions))
        {
            try
            {
                await new AssetRepository(failing).AddAssetAuthorizedAsync(rollback, subject, actor);
                throw new InvalidOperationException("Asset creation ignored audit failure.");
            }
            catch (InvalidOperationException exception) when (exception.Message == "Injected Asset audit failure.") { }
        }
        if (await setup.Assets.AnyAsync(asset => asset.Id == rollback.Id) ||
            await setup.AuditEvents.AnyAsync(audit => audit.TargetId == rollback.Id.ToString()))
            throw new InvalidOperationException("Failed Asset creation left data or audit.");
        // The application precheck also reports an existing key as a conflict.
        await ExpectFailureAsync(async () =>
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            await new AssetRepository(db).AddAssetTypeAuthorizedAsync(type with { Id = Guid.NewGuid() }, adminSubject, adminActor);
        }, FailureKind.Conflict);

        // Hide, restore and delete: the hide authority is separate, a hidden type takes no new Assets, its key
        // cannot be silently redefined, and only a never-used type may be deleted.
        await ExpectFailureAsync(() => HideAsync(type.Id, "nope", subject, actor), FailureKind.Authorization);
        await HideAsync(type.Id, "superseded", adminSubject, adminActor);
        await using (var check = new WellSiteAutoPilotDbContext(options))
        {
            var hidden = await check.AssetTypes.AsNoTracking().SingleAsync(item => item.Id == type.Id);
            if (hidden.IsActive || hidden.HiddenNote != "superseded" || hidden.HiddenBy != adminActor.IdentityName ||
                hidden.HiddenAtUtc is null || !await check.AuditEvents.AnyAsync(audit => audit.Action == "asset-type.hidden" &&
                    audit.TargetId == type.Id.ToString()))
                throw new InvalidOperationException("Hiding an Asset Type lost its state or audit.");
        }
        await ExpectFailureAsync(() => HideAsync(type.Id, new string('x', 201), adminSubject, adminActor), FailureKind.Validation);
        await ExpectFailureAsync(async () =>
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            await new AssetRepository(db).AddAssetAuthorizedAsync(NewAsset(parentA.Id), subject, actor);
        }, FailureKind.NotFound);
        await ExpectFailureAsync(async () =>
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            await new AssetService(new AssetRepository(db), TimeProvider.System)
                .CreateAssetTypeAuthorizedAsync(new(key, "Again", schema), adminSubject, adminActor);
        }, FailureKind.Conflict);
        await ExpectFailureAsync(() => UnhideAsync(type.Id, subject, actor), FailureKind.Authorization);
        await UnhideAsync(type.Id, adminSubject, adminActor);
        await using (var check = new WellSiteAutoPilotDbContext(options))
        {
            var restored = await check.AssetTypes.AsNoTracking().SingleAsync(item => item.Id == type.Id);
            if (!restored.IsActive || restored.HiddenNote is not null || restored.HiddenAtUtc is not null || restored.HiddenBy is not null)
                throw new InvalidOperationException("Restoring an Asset Type left hidden state behind.");
        }
        await ExpectFailureAsync(() => DeleteAsync(type.Id, adminSubject, adminActor), FailureKind.Conflict);
        var spare = await service.CreateAssetTypeAuthorizedAsync(new(key + "-spare", "Spare type", schema), adminSubject, adminActor);
        await ExpectFailureAsync(() => DeleteAsync(spare.Id, subject, actor), FailureKind.Authorization);
        await DeleteAsync(spare.Id, adminSubject, adminActor);
        await using (var check = new WellSiteAutoPilotDbContext(options))
            if (await check.AssetTypes.AnyAsync(item => item.Id == spare.Id))
                throw new InvalidOperationException("Deleting an unused Asset Type left the row.");

        var pause = new PauseSave();
        var pausedOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(pause).Options;
        await using var writer = new WellSiteAutoPilotDbContext(pausedOptions);
        var pending = NewAsset(parentA.Id);
        var mutation = new AssetRepository(writer).AddAssetAuthorizedAsync(pending, subject, actor);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var connection = new NpgsqlConnectionStringBuilder(setup.Database.GetConnectionString()!)
        { ApplicationName = "wsa-asset-grant-" + Guid.NewGuid().ToString("N"), CommandTimeout = 15 };
        await using var revoker = new NpgsqlConnection(connection.ConnectionString);
        await revoker.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE security.access_grants SET \"IsActive\" = FALSE WHERE \"Id\" = $1", revoker);
        command.Parameters.AddWithValue(grantId);
        var revocation = command.ExecuteNonQueryAsync();
        try
        {
            await using var observer = new NpgsqlConnection(setup.Database.GetConnectionString());
            await observer.OpenAsync();
            await using var waiting = new NpgsqlCommand("""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = $1
                AND wait_event_type = 'Lock' AND cardinality(pg_blocking_pids(pid)) > 0)
                """, observer);
            waiting.Parameters.AddWithValue(connection.ApplicationName!);
            var timer = Stopwatch.StartNew();
            while (!Equals(await waiting.ExecuteScalarAsync(), true))
            {
                if (revocation.IsCompleted || timer.Elapsed > TimeSpan.FromSeconds(8))
                    throw new InvalidOperationException("Asset creation did not hold its parent's grant until commit.");
                await Task.Delay(20);
            }
        }
        finally { pause.Release.TrySetResult(); }
        await mutation;
        if (await revocation != 1 || !await setup.Assets.AnyAsync(asset => asset.Id == pending.Id))
            throw new InvalidOperationException("Authorized Asset creation/revocation ordering was lost.");
        await ExpectFailureAsync(() => CreateAsync(parentA.Id), FailureKind.Authorization);
        Console.WriteLine("Grant-authorized Asset/type/root/child creation, cross-scope denial, exact noninheritance, held schema validation, duplicate-key conflict, atomic audit rollback and revocation lock wait passed.");

        // The enclosing bootstrap test owns and drops this isolated database.
        // Remove grants so its subsequent initialization-marker checks remain
        // about bootstrap state rather than these fixture assignments.
        await setup.AccessGrants.Where(item => item.Id == grantId || item.Id == readGrantId).ExecuteDeleteAsync();
        async Task<Asset> CreateAsync(Guid? parent)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            return await new AssetService(new AssetRepository(db), TimeProvider.System)
                .CreateAssetAuthorizedAsync(new(type.Id, key, parent, """{"rpm":1}"""), subject, actor);
        }
        async Task HideAsync(Guid id, string? note, AccessSubject who, SecurityActorContext by)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            await new AssetRepository(db).SetAssetTypeHiddenAuthorizedAsync(id, true, note, who, by);
        }
        async Task UnhideAsync(Guid id, AccessSubject who, SecurityActorContext by)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            await new AssetRepository(db).SetAssetTypeHiddenAuthorizedAsync(id, false, null, who, by);
        }
        async Task DeleteAsync(Guid id, AccessSubject who, SecurityActorContext by)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            await new AssetRepository(db).DeleteAssetTypeAuthorizedAsync(id, who, by);
        }
        Asset NewAsset(Guid? parent) => new(Guid.NewGuid(), type.Id, key, parent, """{"rpm":1}""", true, now);
    }

    private static async Task ExpectFailureAsync(Func<Task> action, FailureKind kind)
    {
        try { await action(); throw new InvalidOperationException("Invalid Asset creation succeeded."); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == kind) { }
    }
    private sealed class RejectAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected Asset audit failure.");
    }
    private sealed class PauseSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }
}
