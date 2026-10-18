using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Logic;
using WellSiteAutoPilot.Persistence.Security;

internal static class AuthorizedModuleCatalogChecks
{
    public static async Task<int> RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options,
        AccessSubject subject, SecurityActorContext actor, Guid roleId, string sourceManifest)
    {
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var json = JsonNode.Parse(sourceManifest)!.AsObject();
        var moduleId = actor.IdentityName + ".catalog";
        json["moduleId"] = moduleId;
        var command = new RegisterLogicModuleCommand(json.ToJsonString(), new string('2', 64), LogicModuleTrustStatus.TrustedPublisher);
        setup.RolePermissions.Add(new RolePermissionEntity { RoleId = roleId, Permission = WellSitePermissions.LogicManage });
        await setup.SaveChangesAsync();
        // Resource authority, including explicit AllAssets, never supplies
        // deployment-wide catalog rights even when it has logic.manage.
        await ExpectAsync(() => RegisterAsync(command), FailureKind.Authorization);
        var globalGrant = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        setup.AccessGrants.Add(new AccessGrantEntity
        {
            Id = globalGrant, RoleId = roleId, ScopeKind = "AllAssets", Revision = 1, IsActive = true,
            SubjectKind = subject.WindowsGroups is null ? "User" : "WindowsGroup",
            UserId = subject.WindowsGroups is null ? subject.UserId : null,
            GroupSid = subject.WindowsGroups?.GroupSids.Single(), CreatedAtUtc = now, UpdatedAtUtc = now
        });
        await setup.SaveChangesAsync();
        await ExpectAsync(() => RegisterAsync(command), FailureKind.Authorization);
        await setup.AccessGrants.Where(item => item.Id == globalGrant).ExecuteUpdateAsync(set => set.SetProperty(item => item.ScopeKind, "DeploymentWide"));
        if (subject.WindowsGroups is not null)
            await ExpectAsync(() => RegisterAsync(command, proof: new(subject.UserId)), FailureKind.Authorization);
        var first = await RegisterAsync(command);
        if (!first.IsEnabled || (await RegisterAsync(command)).Id != first.Id)
            throw new InvalidOperationException("Authorized registration or idempotent catalog result was incorrect.");
        var unchangedTrust = await RegisterAsync(command with { TrustStatus = LogicModuleTrustStatus.Untrusted });
        if (unchangedTrust.Id != first.Id || unchangedTrust.TrustStatus != first.TrustStatus || !unchangedTrust.IsEnabled)
            throw new InvalidOperationException("Idempotent registration changed installed approval state.");
        await setup.LogicModules.Where(item => item.Id == first.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsEnabled, false));
        if ((await RegisterAsync(command)).IsEnabled)
            throw new InvalidOperationException("Idempotent registration re-enabled a retired module.");
        await setup.LogicModules.Where(item => item.Id == first.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsEnabled, true));
        await ExpectAsync(() => RegisterAsync(command with { PackageSha256 = new string('3', 64) }), FailureKind.Conflict);
        var changed = JsonNode.Parse(command.ManifestJson)!.AsObject();
        changed["displayName"] = "Changed content";
        await ExpectAsync(() => RegisterAsync(command with { ManifestJson = changed.ToJsonString() }), FailureKind.Conflict);
        await setup.RolePermissions.Where(item => item.RoleId == roleId && item.Permission == WellSitePermissions.LogicManage).ExecuteDeleteAsync();
        await ExpectAsync(() => RegisterAsync(command), FailureKind.Authorization);
        setup.ChangeTracker.Clear();
        setup.RolePermissions.Add(new RolePermissionEntity { RoleId = roleId, Permission = WellSitePermissions.LogicManage });
        await setup.SaveChangesAsync();

        json["version"] = "1.0.1";
        var second = command with { ManifestJson = json.ToJsonString() };
        var failingOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(new FailAfterSave()).Options;
        try
        {
            await RegisterAsync(second, requestOptions: failingOptions);
            throw new InvalidOperationException("Catalog failure injection did not run.");
        }
        catch (InvalidOperationException exception) when (exception.Message == "catalog-save-probe") { }
        if (await setup.LogicModules.AnyAsync(item => item.ModuleId == moduleId && item.Version == "1.0.1") ||
            await setup.AuditEvents.CountAsync(item => item.ActorUserId == subject.UserId && item.Action == "logic-module.registered") != 1)
            throw new InvalidOperationException("Failed catalog registration did not roll back module/audit.");

        var pause = new PauseAfterSave();
        var pausedOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(pause).Options;
        var write = RegisterAsync(second, requestOptions: pausedOptions);
        InstalledLogicModule secondModule;
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var connection = new NpgsqlConnectionStringBuilder(setup.Database.GetConnectionString()!)
            { ApplicationName = "catalog-revoke-" + Guid.NewGuid().ToString("N"), CommandTimeout = 15 };
            await using var revoker = new NpgsqlConnection(connection.ConnectionString);
            await revoker.OpenAsync();
            await using var revokeCommand = new NpgsqlCommand("UPDATE security.access_grants SET \"IsActive\" = FALSE WHERE \"Id\" = $1", revoker);
            revokeCommand.Parameters.AddWithValue(globalGrant);
            var revoke = revokeCommand.ExecuteNonQueryAsync();
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
                    if (revoke.IsCompleted || timer.Elapsed > TimeSpan.FromSeconds(8))
                        throw new InvalidOperationException("Catalog registration did not retain global authority through commit.");
                    await Task.Delay(20);
                }
            }
            finally { pause.Release.TrySetResult(); }
            secondModule = await write;
            if (await revoke != 1) throw new InvalidOperationException("Catalog grant revocation did not complete.");
        }
        finally { pause.Release.TrySetResult(); await write; }
        await ExpectAsync(() => RegisterAsync(command), FailureKind.Authorization);
        await ExpectAsync(() => RegisterAsync(second), FailureKind.Authorization);
        if (await setup.LogicModules.CountAsync(item => item.ModuleId == moduleId) != 2 ||
            await setup.AuditEvents.CountAsync(item => item.TargetId == first.Id.ToString() || item.TargetId == secondModule.Id.ToString()) != 2)
            throw new InvalidOperationException("Catalog idempotency/conflict handling duplicated module/audit state.");
        // Approval: a registered-but-untrusted version can be approved once,
        // retired once, and never approved again; every change is audited with
        // its reason, repeats change nothing, and it needs deployment authority.
        await setup.AccessGrants.Where(item => item.Id == globalGrant)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, true));
        json["version"] = "1.0.2";
        var untrusted = await RegisterAsync(command with
        {
            ManifestJson = json.ToJsonString(), TrustStatus = LogicModuleTrustStatus.Untrusted
        });
        if (untrusted.IsEnabled || untrusted.TrustStatus != LogicModuleTrustStatus.Untrusted)
            throw new InvalidOperationException("An untrusted module was registered as usable.");
        await ExpectAsync(() => ChangeAsync(untrusted.Version, LogicModuleApprovalAction.Approve, " "), FailureKind.Validation);
        await ExpectAsync(() => ChangeAsync("9.9.9", LogicModuleApprovalAction.Approve, "Reviewed"), FailureKind.NotFound);
        await setup.RolePermissions.Where(item => item.RoleId == roleId && item.Permission == WellSitePermissions.LogicManage).ExecuteDeleteAsync();
        await ExpectAsync(() => ChangeAsync(untrusted.Version, LogicModuleApprovalAction.Approve, "Reviewed"), FailureKind.Authorization);
        setup.ChangeTracker.Clear();
        setup.RolePermissions.Add(new RolePermissionEntity { RoleId = roleId, Permission = WellSitePermissions.LogicManage });
        await setup.SaveChangesAsync();
        var approved = await ChangeAsync(untrusted.Version, LogicModuleApprovalAction.Approve, " Reviewed the package ");
        if (approved.TrustStatus != LogicModuleTrustStatus.CustomerApproved || !approved.IsEnabled ||
            (await ChangeAsync(untrusted.Version, LogicModuleApprovalAction.Approve, "Again")).Id != untrusted.Id ||
            await ApprovalAuditCountAsync(untrusted.Id, "logic-module.approved") != 1 ||
            !(await setup.AuditEvents.SingleAsync(item => item.TargetId == untrusted.Id.ToString() && item.Action == "logic-module.approved"))
                .DetailsJson!.Contains("Reviewed the package", StringComparison.Ordinal))
            throw new InvalidOperationException("Approving a module did not enable it exactly once with its reason.");
        var retired = await ChangeAsync(untrusted.Version, LogicModuleApprovalAction.Retire, "Superseded");
        var stored = await setup.LogicModules.AsNoTracking().SingleAsync(item => item.Id == untrusted.Id);
        if (retired.IsEnabled || stored.IsEnabled || stored.TrustStatus != nameof(LogicModuleTrustStatus.CustomerApproved) ||
            (await ChangeAsync(untrusted.Version, LogicModuleApprovalAction.Retire, "Again")).IsEnabled ||
            await ApprovalAuditCountAsync(untrusted.Id, "logic-module.retired") != 1)
            throw new InvalidOperationException("Retiring a module did not disable it exactly once.");
        await ExpectAsync(() => ChangeAsync(untrusted.Version, LogicModuleApprovalAction.Approve, "Back"), FailureKind.Conflict);
        json["version"] = "1.0.3";
        var neverApproved = await RegisterAsync(command with
        {
            ManifestJson = json.ToJsonString(), TrustStatus = LogicModuleTrustStatus.Untrusted
        });
        await ExpectAsync(() => ChangeAsync(neverApproved.Version, LogicModuleApprovalAction.Retire, "Nothing"), FailureKind.Conflict);
        await setup.AccessGrants.Where(item => item.Id == globalGrant).ExecuteDeleteAsync();
        await setup.LogicModules.Where(item => item.ModuleId == moduleId).ExecuteDeleteAsync();
        Console.WriteLine("Catalog writes require deployment-wide direct/group authority, including idempotency, and commit module/audit atomically with revocation fencing; approval and retirement are audited, idempotent and final.");
        // Registrations of 1.0.0, 1.0.1, 1.0.2 and 1.0.3 plus one approval and one retirement.
        return 6;

        async Task<InstalledLogicModule> ChangeAsync(string version, LogicModuleApprovalAction action, string reason)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            var approvals = new LogicModuleApprovalService(new LogicModuleCatalogRepository(db));
            return action == LogicModuleApprovalAction.Approve
                ? await approvals.ApproveAsync(moduleId, version, reason, subject, actor)
                : await approvals.RetireAsync(moduleId, version, reason, subject, actor);
        }

        Task<int> ApprovalAuditCountAsync(Guid id, string action) =>
            setup.AuditEvents.CountAsync(item => item.TargetId == id.ToString() && item.Action == action);

        async Task<InstalledLogicModule> RegisterAsync(RegisterLogicModuleCommand registration, AccessSubject? proof = null,
            DbContextOptions<WellSiteAutoPilotDbContext>? requestOptions = null)
        {
            await using var db = new WellSiteAutoPilotDbContext(requestOptions ?? options);
            return await new LogicModuleCatalogService(new LogicModuleCatalogRepository(db), TimeProvider.System)
                .RegisterAuthorizedAsync(registration, proof ?? subject, actor);
        }
    }

    private static async Task ExpectAsync(Func<Task> operation, FailureKind kind)
    {
        try { await operation(); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == kind) { return; }
        throw new InvalidOperationException($"Expected catalog refusal: {kind}.");
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("catalog-save-probe");
    }
    private sealed class PauseAfterSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }
}
