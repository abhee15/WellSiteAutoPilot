using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Logic;

internal static class AuthorizedConfigurationLifecycleChecks
{
    public static async Task<int> RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid id,
        AccessSubject subject, SecurityActorContext actor, Guid manageGrant, Guid readGrant, Guid outsideAsset,
        Guid typeId, string moduleId)
    {
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var definition = await new ConfiguredLogicRepository(setup).GetAuthorizedAsync(subject, id) ?? throw new InvalidOperationException();
        var first = definition.Revisions.Single(item => item.RevisionNumber == 1);
        var second = definition.Revisions.Single(item => item.RevisionNumber == 2);
        var baseline = await AuditCountAsync();
        await ExpectAsync(() => WriteAsync(repository => repository.SetRevisionValidatedAuthorizedAsync(
            first with { ParametersJson = "{\"changed\":true}" }, subject, actor)), FailureKind.Conflict);
        foreach (var grant in new[] { manageGrant, readGrant })
        {
            await SetGrantAsync(grant, false);
            try
            {
                await ExpectAsync(() => WriteAsync(repository => repository.SetRevisionValidatedAuthorizedAsync(first, subject, actor)), FailureKind.Authorization);
            }
            finally { await SetGrantAsync(grant, true); }
        }
        if (subject.WindowsGroups is not null)
            await ExpectAsync(() => WriteAsync(repository => repository.SetRevisionValidatedAuthorizedAsync(first, new(subject.UserId), actor)), FailureKind.Authorization);

        // A newer revision cannot hide an older, out-of-scope input Asset.
        setup.ConfiguredLogicDataBindings.Add(new()
        {
            RevisionId = first.Id, RequirementId = "foreign-input", AssetId = outsideAsset,
            ProviderId = "test", ProviderAssetExternalId = "test", ProviderMappingJson = "{}"
        });
        await setup.SaveChangesAsync();
        try
        {
            await ExpectAsync(() => WriteAsync(repository => repository.SetRevisionValidatedAuthorizedAsync(second, subject, actor)), FailureKind.Authorization);
            if (await new ConfiguredLogicRepository(setup).GetAuthorizedAsync(subject, id) is not null)
                throw new InvalidOperationException("Lifecycle input scope escaped the authorized reader.");
        }
        finally
        {
            await setup.ConfiguredLogicDataBindings.Where(item => item.RevisionId == first.Id && item.RequirementId == "foreign-input").ExecuteDeleteAsync();
            setup.ChangeTracker.Clear();
        }
        await setup.LogicModules.Where(item => item.ModuleId == moduleId).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsEnabled, false));
        try
        {
            await ExpectAsync(() => WriteAsync(repository => repository.SetRevisionValidatedAuthorizedAsync(first, subject, actor)), FailureKind.Conflict);
        }
        finally { await setup.LogicModules.Where(item => item.ModuleId == moduleId).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsEnabled, true)); }
        await setup.AssetTypes.Where(item => item.Id == typeId).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, false));
        try
        {
            await ExpectAsync(() => WriteAsync(repository => repository.SetRevisionValidatedAuthorizedAsync(first, subject, actor)), FailureKind.Conflict);
        }
        finally { await setup.AssetTypes.Where(item => item.Id == typeId).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, true)); }

        await ExpectSaveRollbackAsync(first, false, null);
        await ServiceAsync(service => service.ValidateRevisionAuthorizedAsync(id, first.Id, subject, actor));
        var validatedFirst = (await ReadAsync()).Revisions.Single(item => item.Id == first.Id);
        await ExpectAsync(() => ServiceAsync(service => service.ActivateRevisionAuthorizedAsync(id, first.Id, "", subject, actor)), FailureKind.Validation);
        await ExpectAsync(() => ServiceAsync(service => service.ActivateRevisionAuthorizedAsync(id, first.Id, Guid.NewGuid().ToString(), subject, actor)), FailureKind.Conflict);
        await ExpectSaveRollbackAsync(validatedFirst, true, null);
        if ((await ReadAsync()).ActiveRevisionId is not null) throw new InvalidOperationException("Failed activation changed the active revision.");
        await ServiceAsync(service => service.ActivateRevisionAuthorizedAsync(id, first.Id, "none", subject, actor));
        await ServiceAsync(service => service.ValidateRevisionAuthorizedAsync(id, second.Id, subject, actor));
        var validatedSecond = (await ReadAsync()).Revisions.Single(item => item.Id == second.Id);
        await ExpectAsync(() => WriteAsync(repository => repository.ActivateRevisionAuthorizedAsync(validatedSecond, null, subject, actor)), FailureKind.Conflict);

        // Module retirement must wait for activation/audit commit, just as grant
        // revocation does. Observe an actual server lock wait, not elapsed time.
        var pause = new PauseAfterSave();
        var pausedOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(pause).Options;
        await using (var writer = new WellSiteAutoPilotDbContext(pausedOptions))
        {
            var write = new ConfiguredLogicRepository(writer).ActivateRevisionAuthorizedAsync(validatedSecond, first.Id, subject, actor);
            try
            {
                await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
                var connection = new NpgsqlConnectionStringBuilder(setup.Database.GetConnectionString()!)
                { ApplicationName = "lifecycle-retire-" + Guid.NewGuid().ToString("N"), CommandTimeout = 15 };
                await using var retire = new NpgsqlConnection(connection.ConnectionString);
                await retire.OpenAsync();
                await using var command = new NpgsqlCommand("UPDATE logic.logic_modules SET \"IsEnabled\" = FALSE WHERE \"ModuleId\" = $1", retire);
                command.Parameters.AddWithValue(moduleId);
                var retirement = command.ExecuteNonQueryAsync();
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
                        if (retirement.IsCompleted || timer.Elapsed > TimeSpan.FromSeconds(8))
                            throw new InvalidOperationException("Activation did not retain module authority through commit.");
                        await Task.Delay(20);
                    }
                }
                finally { pause.Release.TrySetResult(); }
                await write;
                if (await retirement != 1) throw new InvalidOperationException("Module retirement did not complete.");
            }
            finally
            {
                pause.Release.TrySetResult();
                await write;
            }
        }
        await ExpectAsync(() => WriteAsync(repository => repository.ActivateRevisionAuthorizedAsync(validatedSecond, first.Id, subject, actor)), FailureKind.Conflict);
        var third = second with
        {
            Id = Guid.NewGuid(), RevisionNumber = 3, Status = ConfiguredLogicRevisionStatus.Draft,
            ValidatedAtUtc = null, ActivatedAtUtc = null
        };
        await WriteAsync(repository => repository.AddRevisionAuthorizedAsync(third, subject, actor));
        third = (await ReadAsync()).Revisions.Single(item => item.Id == third.Id);
        await ExpectAsync(() => WriteAsync(repository => repository.SetRevisionValidatedAuthorizedAsync(third, subject, actor)), FailureKind.Conflict);
        var active = await ReadAsync();
        if (active.ActiveRevisionId != second.Id || active.Revisions.Single(item => item.Id == first.Id).Status != ConfiguredLogicRevisionStatus.Superseded ||
            active.Revisions.Single(item => item.Id == second.Id).Status != ConfiguredLogicRevisionStatus.Active ||
            active.Revisions.Single(item => item.Id == third.Id).Status != ConfiguredLogicRevisionStatus.Draft ||
            await AuditCountAsync() != baseline + 5)
            throw new InvalidOperationException("Lifecycle status, active revision or atomic audit state was incorrect.");
        Console.WriteLine("Grant-authorized lifecycle covers paired scope, reviewed snapshots, dependency retirement, rollback, active-token conflict and atomic audit.");
        return 5;

        Task<int> AuditCountAsync() => setup.AuditEvents.CountAsync(item => item.ActorUserId == subject.UserId && item.CorrelationId == actor.CorrelationId);
        Task<int> SetGrantAsync(Guid grant, bool active) => setup.AccessGrants.Where(item => item.Id == grant).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, active));
        async Task<ConfiguredLogicDefinition> ReadAsync()
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            return await new ConfiguredLogicRepository(db).GetAuthorizedAsync(subject, id) ?? throw new InvalidOperationException();
        }
        async Task WriteAsync(Func<ConfiguredLogicRepository, Task> write)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            await write(new ConfiguredLogicRepository(db));
        }
        async Task ServiceAsync(Func<ConfiguredLogicService, Task> operation)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            await operation(new ConfiguredLogicService(new ConfiguredLogicRepository(db), new AssetRepository(db), new LogicModuleCatalogRepository(db), TimeProvider.System));
        }
        async Task ExpectSaveRollbackAsync(ConfiguredLogicRevision revision, bool activate, Guid? expectedActive)
        {
            var failureOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(new FailAfterSave()).Options;
            await using var db = new WellSiteAutoPilotDbContext(failureOptions);
            try
            {
                var repository = new ConfiguredLogicRepository(db);
                if (activate) await repository.ActivateRevisionAuthorizedAsync(revision, expectedActive, subject, actor);
                else await repository.SetRevisionValidatedAuthorizedAsync(revision, subject, actor);
                throw new InvalidOperationException("Lifecycle save failure was not observed.");
            }
            catch (InvalidOperationException exception) when (exception.Message == "lifecycle-save-probe") { }
            var persisted = (await ReadAsync()).Revisions.Single(item => item.Id == revision.Id);
            if (persisted.Status != revision.Status || await AuditCountAsync() != baseline + (activate ? 1 : 0))
                throw new InvalidOperationException("Lifecycle failure did not roll back status and audit together.");
        }
    }

    private static async Task ExpectAsync(Func<Task> operation, FailureKind kind)
    {
        try { await operation(); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == kind) { return; }
        throw new InvalidOperationException($"Expected lifecycle refusal: {kind}.");
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("lifecycle-save-probe");
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
