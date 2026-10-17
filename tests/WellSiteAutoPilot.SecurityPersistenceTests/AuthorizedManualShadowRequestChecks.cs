using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Logic;
using WellSiteAutoPilot.Persistence.Security;

internal static class AuthorizedManualShadowRequestChecks
{
    public static async Task<int> RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid configurationId,
        AccessSubject subject, SecurityActorContext actor, Guid requestRole, Guid requestGrant, Guid readGrant, Guid outsideAsset, string moduleId)
    {
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var definition = await new ConfiguredLogicRepository(setup).GetAuthorizedAsync(subject, configurationId) ?? throw new InvalidOperationException();
        var revisionId = definition.ActiveRevisionId ?? throw new InvalidOperationException();
        await setup.LogicModules.Where(item => item.ModuleId == moduleId).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsEnabled, true));
        await ExpectAsync(() => RequestAsync(Guid.NewGuid()), FailureKind.Authorization);
        setup.RolePermissions.Add(new RolePermissionEntity { RoleId = requestRole, Permission = WellSitePermissions.ExecutionsRequest });
        await setup.SaveChangesAsync();
        await ExpectAsync(() => RequestAsync(Guid.Empty), FailureKind.Validation);
        if (subject.WindowsGroups is not null)
            await ExpectAsync(() => RequestAsync(Guid.NewGuid(), proof: new(subject.UserId)), FailureKind.Authorization);
        var failedKey = Guid.NewGuid();
        var failedId = ManualShadowRequestService.DeriveExecutionId(subject.UserId, failedKey);
        var failingOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(new FailAfterSave()).Options;
        try
        {
            await RequestAsync(failedKey, requestOptions: failingOptions);
            throw new InvalidOperationException("Request failure injection did not run.");
        }
        catch (InvalidOperationException exception) when (exception.Message == "request-save-probe") { }
        if (await setup.Executions.AnyAsync(item => item.Id == failedId) ||
            await setup.OutboxMessages.AnyAsync(item => item.Payload.Contains(failedId.ToString())) ||
            await setup.AuditEvents.AnyAsync(item => item.TargetId == failedId.ToString()))
            throw new InvalidOperationException("Failed manual request left an execution, outbox or audit.");

        var key = Guid.NewGuid();
        var receipt = await RequestAsync(key);
        if (receipt.ExecutionId != ManualShadowRequestService.DeriveExecutionId(subject.UserId, key) ||
            receipt.ConfiguredLogicId != configurationId || receipt.ConfigurationRevisionId != revisionId || receipt.ContractVersion != 2)
            throw new InvalidOperationException("Manual request receipt identity was incorrect.");
        await ExpectAsync(() => RequestAsync(Guid.NewGuid()), FailureKind.Conflict);
        await setup.Executions.Where(item => item.Id == receipt.ExecutionId).ExecuteUpdateAsync(set => set
            .SetProperty(item => item.Status, "Completed").SetProperty(item => item.OutputJson, "{\"restricted\":true}"));
        if (await RequestAsync(key) != receipt) throw new InvalidOperationException("Idempotent replay changed the request receipt.");
        // This caller has no executions.read grant, and the return type must
        // never acquire payload/status/output fields from a completed execution.
        if (typeof(ManualShadowRequestReceipt).GetProperties().Any(item => item.Name is "Status" or "OutputJson" or "RequestPayloadJson" or "ResultCode"))
            throw new InvalidOperationException("Request receipt exposes execution-read data.");
        await ExpectAsync(() => RequestAsync(key, definition.Revisions.Single(item => item.RevisionNumber == 1).Id), FailureKind.Conflict);
        foreach (var grant in new[] { requestGrant, readGrant })
        {
            await SetGrantAsync(grant, false);
            try { await ExpectAsync(() => RequestAsync(key), FailureKind.Authorization); }
            finally { await SetGrantAsync(grant, true); }
        }
        var sourceScope = await setup.ExecutionAssetScopes.AsNoTracking().FirstAsync(item => item.ExecutionId == receipt.ExecutionId);
        setup.ExecutionAssetScopes.Add(new() { ExecutionId = receipt.ExecutionId, LogicInstanceId = configurationId, AssetId = outsideAsset });
        await setup.SaveChangesAsync();
        try { await ExpectAsync(() => RequestAsync(key), FailureKind.Authorization); }
        finally
        {
            await setup.ExecutionAssetScopes.Where(item => item.ExecutionId == receipt.ExecutionId && item.AssetId == outsideAsset).ExecuteDeleteAsync();
            setup.ChangeTracker.Clear();
        }
        var payload = await setup.Executions.Where(item => item.Id == receipt.ExecutionId).Select(item => item.RequestPayloadJson).SingleAsync();
        var snapshotOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = JsonSerializer.Deserialize<ExecutionRequestedV2>(payload!, snapshotOptions) ?? throw new InvalidOperationException();
        var corrupt = JsonSerializer.Serialize(snapshot with
        {
            Assets = [.. snapshot.Assets, new("foreign", outsideAsset, "{}")]
        }, snapshotOptions);
        await setup.Executions.Where(item => item.Id == receipt.ExecutionId).ExecuteUpdateAsync(set => set.SetProperty(item => item.RequestPayloadJson, corrupt));
        try { await ExpectAsync(() => RequestAsync(key), FailureKind.Conflict); }
        finally { await setup.Executions.Where(item => item.Id == receipt.ExecutionId).ExecuteUpdateAsync(set => set.SetProperty(item => item.RequestPayloadJson, payload)); }
        await setup.LogicModules.Where(item => item.ModuleId == moduleId).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsEnabled, false));
        try
        {
            if (await RequestAsync(key) != receipt) throw new InvalidOperationException("Module retirement hid an authorized historical receipt.");
            await ExpectAsync(() => RequestAsync(Guid.NewGuid()), FailureKind.Conflict);
        }
        finally { await setup.LogicModules.Where(item => item.ModuleId == moduleId).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsEnabled, true)); }

        var pause = new PauseAfterSave();
        var pausedOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(pause).Options;
        var secondKey = Guid.NewGuid();
        var secondWrite = RequestAsync(secondKey, requestOptions: pausedOptions);
        ManualShadowRequestReceipt secondReceipt;
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var connection = new NpgsqlConnectionStringBuilder(setup.Database.GetConnectionString()!)
            { ApplicationName = "request-revoke-" + Guid.NewGuid().ToString("N"), CommandTimeout = 15 };
            await using var revoker = new NpgsqlConnection(connection.ConnectionString);
            await revoker.OpenAsync();
            await using var command = new NpgsqlCommand("UPDATE security.access_grants SET \"IsActive\" = FALSE WHERE \"Id\" = $1", revoker);
            command.Parameters.AddWithValue(requestGrant);
            var revoke = command.ExecuteNonQueryAsync();
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
                        throw new InvalidOperationException("Manual request did not retain authority through outbox/audit commit.");
                    await Task.Delay(20);
                }
            }
            finally { pause.Release.TrySetResult(); }
            secondReceipt = await secondWrite;
            if (await revoke != 1) throw new InvalidOperationException("Request revocation did not complete.");
        }
        finally
        {
            pause.Release.TrySetResult();
            await secondWrite;
        }
        await ExpectAsync(() => RequestAsync(secondKey), FailureKind.Authorization);
        await ExpectAsync(() => RequestAsync(Guid.NewGuid()), FailureKind.Authorization);
        await SetGrantAsync(requestGrant, true);
        if (await setup.Executions.CountAsync(item => item.LogicInstanceId == configurationId) != 2 ||
            await setup.OutboxMessages.CountAsync(item => item.Payload.Contains(moduleId)) != 2 ||
            await setup.AuditEvents.CountAsync(item => item.TargetId == receipt.ExecutionId.ToString() || item.TargetId == secondReceipt.ExecutionId.ToString()) != 2 ||
            sourceScope.AssetId == outsideAsset)
            throw new InvalidOperationException("Manual request/replay duplicated state or lost its atomic outbox/audit.");
        await setup.OutboxMessages.Where(item => item.Payload.Contains(moduleId)).ExecuteDeleteAsync();
        await setup.Executions.Where(item => item.LogicInstanceId == configurationId).ExecuteDeleteAsync();
        Console.WriteLine("Authorized manual requests retain persisted scope/authority, atomic outbox/audit and receipt-only replay after fresh checks.");
        return 2;

        Task<int> SetGrantAsync(Guid grant, bool active) => setup.AccessGrants.Where(item => item.Id == grant)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, active));
        async Task<ManualShadowRequestReceipt> RequestAsync(Guid requestId, Guid? targetRevision = null,
            AccessSubject? proof = null, DbContextOptions<WellSiteAutoPilotDbContext>? requestOptions = null)
        {
            await using var db = new WellSiteAutoPilotDbContext(requestOptions ?? options);
            var configurations = new ConfiguredLogicService(new ConfiguredLogicRepository(db), new AssetRepository(db),
                new LogicModuleCatalogRepository(db), TimeProvider.System);
            return await new ManualShadowRequestService(new ExecutionRepository(db), configurations, TimeProvider.System)
                .RequestAuthorizedAsync(configurationId, targetRevision ?? revisionId, requestId, proof ?? subject, actor);
        }
    }

    private static async Task ExpectAsync(Func<Task> operation, FailureKind kind)
    {
        try { await operation(); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == kind) { return; }
        throw new InvalidOperationException($"Expected manual-request refusal: {kind}.");
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("request-save-probe");
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
