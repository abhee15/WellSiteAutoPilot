using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Logic;

var connectionString = Environment.GetEnvironmentVariable("WSA_DATABASE_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("WSA_DATABASE_CONNECTION_STRING is required.");
}

var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var dbContext = new WellSiteAutoPilotDbContext(options);

var clock = new MutableTimeProvider(
    new DateTimeOffset(2026, 10, 8, 12, 10, 30, TimeSpan.Zero));

var assetRepository = new AssetRepository(dbContext);
var configuredLogicRepository = new ConfiguredLogicRepository(dbContext);
var executionRepository = new ExecutionRepository(dbContext);
var moduleRepository = new LogicModuleCatalogRepository(dbContext);

var assetService = new AssetService(assetRepository, clock);
var moduleCatalog = new LogicModuleCatalogService(moduleRepository, clock);
var configuredLogicService = new ConfiguredLogicService(
    configuredLogicRepository,
    assetRepository,
    moduleRepository,
    clock);
var executionService = new ExecutionService(executionRepository, clock);
var scheduler = new ScheduledShadowSchedulerService(
    configuredLogicRepository,
    moduleRepository,
    executionRepository,
    executionService,
    clock);

var wellType = await assetService.CreateAssetTypeAsync(
    new CreateAssetTypeCommand(
        $"scheduled-well-{Guid.NewGuid():N}",
        "Scheduled Shadow Well",
        """{"type":"object"}"""));

var well = await assetService.CreateAssetAsync(
    new CreateAssetCommand(
        wellType.Id,
        "Scheduler Well 101",
        null,
        """{"field":"scheduler"}"""));

var manifest = $$"""
{
  "manifestVersion": 1,
  "moduleId": "weatherford.scheduler-shadow-test",
  "version": "1.0.0",
  "displayName": "Scheduler Shadow Test",
  "publisher": "Weatherford",
  "runtime": "DotNet",
  "executionProfile": "ScheduledOneShot",
  "entryPoint": "Weatherford.SchedulerShadowTest.EntryPoint",
  "minimumSdkVersion": "1.0.0",
  "assetRequirements": [
    {
      "role": "well",
      "minimumCount": 1,
      "maximumCount": 1,
      "requiredAssetTypeKey": "{{wellType.Key}}",
      "requiredTraits": []
    }
  ],
  "dataRequirements": [
    {
      "id": "pump-fillage",
      "assetRole": "well",
      "quantity": "PumpFillage",
      "access": "Current",
      "canonicalUnit": "%",
      "maximumAgeSeconds": 60,
      "allowUncertainQuality": false
    }
  ],
  "commandRequirements": []
}
""";

await moduleCatalog.RegisterAsync(
    new RegisterLogicModuleCommand(
        manifest,
        new string('a', 64),
        LogicModuleTrustStatus.TrustedPublisher));

var scheduleStart = new DateTimeOffset(
    2026,
    10,
    8,
    12,
    0,
    0,
    TimeSpan.Zero);

var configuredLogic = await configuredLogicService.CreateAsync(
    new CreateConfiguredLogicCommand(
        "Scheduled Pump Fillage Observer",
        manifest,
        """{"targetFillage":75}""",
        [
            new ConfiguredLogicAssetBindingCommand(
                "well",
                well.Id,
                """{"targetFillage":80}""")
        ],
        [
            new ConfiguredLogicDataBindingCommand(
                "pump-fillage",
                well.Id,
                "simulator",
                "WELL-101",
                "{}")
        ],
        new ConfiguredLogicScheduleCommand(
            true,
            60,
            scheduleStart,
            "UTC")));

var firstRevision = configuredLogic.Revisions.Single();
await configuredLogicService.ValidateRevisionAsync(
    configuredLogic.Id,
    firstRevision.Id);
await configuredLogicService.ActivateRevisionAsync(
    configuredLogic.Id,
    firstRevision.Id);

var firstSweep = await scheduler.DispatchDueAsync(100);
var firstDecision = firstSweep.Decisions.Single();

var expectedFirstOccurrence = new DateTimeOffset(
    2026,
    10,
    8,
    12,
    10,
    0,
    TimeSpan.Zero);

if (firstDecision.Outcome != ScheduledShadowDispatchOutcome.Dispatched ||
    firstDecision.ExecutionId is null ||
    firstDecision.ScheduledForUtc != expectedFirstOccurrence)
{
    throw new InvalidOperationException(
        "Scheduler did not dispatch exactly the latest due occurrence.");
}

var firstExecution = await executionService.GetRequiredAsync(
    firstDecision.ExecutionId.Value);

if (firstExecution.Trigger != ExecutionTriggerKind.Scheduled ||
    firstExecution.RequestContractVersion != 2 ||
    firstExecution.ScheduledForUtc != expectedFirstOccurrence)
{
    throw new InvalidOperationException(
        "Scheduled execution did not preserve V2 trigger metadata.");
}

var scopePersisted = await dbContext.ExecutionAssetScopes
    .AsNoTracking()
    .AnyAsync(
        item => item.ExecutionId == firstExecution.Id &&
                item.LogicInstanceId == configuredLogic.Id &&
                item.AssetId == well.Id);

if (!scopePersisted)
{
    throw new InvalidOperationException(
        "Scheduled execution did not persist its Asset concurrency scope.");
}

var duplicateSweep = await scheduler.DispatchDueAsync(100);
if (duplicateSweep.Decisions.Single().Outcome !=
    ScheduledShadowDispatchOutcome.AlreadyDispatched)
{
    throw new InvalidOperationException(
        "Scheduler did not suppress a duplicate scheduled occurrence.");
}

clock.Advance(TimeSpan.FromMinutes(1));

var overlapSweep = await scheduler.DispatchDueAsync(100);
if (overlapSweep.Decisions.Single().Outcome !=
    ScheduledShadowDispatchOutcome.OverlapBlocked)
{
    throw new InvalidOperationException(
        "Scheduler did not block an overlapping Configured Logic + Asset execution.");
}

await executionRepository.ApplyCompletedAsync(
    Guid.NewGuid(),
    "scheduler-test-result",
    firstExecution.Id,
    firstExecution.RequestedAtUtc,
    clock.GetUtcNow(),
    "SCHEDULER_TEST_COMPLETED",
    "{}");

var secondSweep = await scheduler.DispatchDueAsync(100);
var secondDecision = secondSweep.Decisions.Single();

var expectedSecondOccurrence = new DateTimeOffset(
    2026,
    10,
    8,
    12,
    11,
    0,
    TimeSpan.Zero);

if (secondDecision.Outcome != ScheduledShadowDispatchOutcome.Dispatched ||
    secondDecision.ScheduledForUtc != expectedSecondOccurrence ||
    secondDecision.ExecutionId is null)
{
    throw new InvalidOperationException(
        "Scheduler did not dispatch the next occurrence after overlap cleared.");
}

var secondExecution = await executionService.GetRequiredAsync(
    secondDecision.ExecutionId.Value);

clock.Advance(TimeSpan.FromMinutes(5));

var longDowntimeOverlap = await scheduler.DispatchDueAsync(100);
if (longDowntimeOverlap.Decisions.Single().Outcome !=
    ScheduledShadowDispatchOutcome.OverlapBlocked)
{
    throw new InvalidOperationException(
        "Scheduler did not preserve the no-overlap rule after a multi-interval gap.");
}

await executionRepository.ApplyCompletedAsync(
    Guid.NewGuid(),
    "scheduler-test-result",
    secondExecution.Id,
    secondExecution.RequestedAtUtc,
    clock.GetUtcNow(),
    "SCHEDULER_TEST_COMPLETED",
    "{}");

var resumedSweep = await scheduler.DispatchDueAsync(100);
var resumedDecision = resumedSweep.Decisions.Single();

var expectedResumedOccurrence = new DateTimeOffset(
    2026,
    10,
    8,
    12,
    16,
    0,
    TimeSpan.Zero);

if (resumedDecision.Outcome != ScheduledShadowDispatchOutcome.Dispatched ||
    resumedDecision.ScheduledForUtc != expectedResumedOccurrence)
{
    throw new InvalidOperationException(
        "Scheduler replayed missed intervals instead of dispatching only the latest due occurrence.");
}

var scheduledExecutions = await dbContext.Executions
    .AsNoTracking()
    .Where(item => item.LogicInstanceId == configuredLogic.Id)
    .OrderBy(item => item.ScheduledForUtc)
    .ToArrayAsync();

if (scheduledExecutions.Length != 3)
{
    throw new InvalidOperationException(
        "Scheduler persisted an unexpected number of occurrences.");
}

var occurrenceTimes = scheduledExecutions
    .Select(item => item.ScheduledForUtc)
    .ToArray();

if (occurrenceTimes[0] != expectedFirstOccurrence ||
    occurrenceTimes[1] != expectedSecondOccurrence ||
    occurrenceTimes[2] != expectedResumedOccurrence)
{
    throw new InvalidOperationException(
        "Scheduler persisted an unexpected occurrence sequence.");
}

// Use real persistence and independent asset scopes to prove that a transient
// failure in one candidate cannot starve another due candidate.
var faultedModuleId = $"weatherford.scheduler-fault-{Guid.NewGuid():N}";
var faultedManifest = manifest.Replace(
    "weatherford.scheduler-shadow-test",
    faultedModuleId,
    StringComparison.Ordinal);

await moduleCatalog.RegisterAsync(
    new RegisterLogicModuleCommand(
        faultedManifest,
        new string('b', 64),
        LogicModuleTrustStatus.TrustedPublisher));

var isolatedDefinitions = new List<ConfiguredLogicDefinition>();
var isolationManifests = new[] { faultedManifest, manifest };
var isolationNames = new[]
{
    "AA Scheduler Injected Failure",
    "AB Scheduler Healthy Candidate"
};

for (var index = 0; index < isolationNames.Length; index++)
{
    var isolationAsset = await assetService.CreateAssetAsync(
        new CreateAssetCommand(
            wellType.Id,
            $"Scheduler Isolation Well {index + 1}",
            null,
            """{"field":"scheduler-isolation"}"""));

    var definition = await configuredLogicService.CreateAsync(
        new CreateConfiguredLogicCommand(
            isolationNames[index],
            isolationManifests[index],
            """{"targetFillage":75}""",
            [
                new ConfiguredLogicAssetBindingCommand(
                    "well",
                    isolationAsset.Id,
                    "{}")
            ],
            [
                new ConfiguredLogicDataBindingCommand(
                    "pump-fillage",
                    isolationAsset.Id,
                    "simulator",
                    $"ISOLATION-WELL-{index + 1}",
                    "{}")
            ],
            new ConfiguredLogicScheduleCommand(
                true,
                60,
                scheduleStart,
                "UTC")));

    var revision = definition.Revisions.Single();
    await configuredLogicService.ValidateRevisionAsync(
        definition.Id,
        revision.Id);
    await configuredLogicService.ActivateRevisionAsync(
        definition.Id,
        revision.Id);
    isolatedDefinitions.Add(definition);
}

var failedDefinition = isolatedDefinitions[0];
var healthyDefinition = isolatedDefinitions[1];
var failedRevisionId = failedDefinition.Revisions.Single().Id;
var healthyRevisionId = healthyDefinition.Revisions.Single().Id;

var faultInjectingCatalog = new FaultInjectingModuleCatalogRepository(
    moduleRepository,
    faultedModuleId);
var isolatedScheduler = new ScheduledShadowSchedulerService(
    configuredLogicRepository,
    faultInjectingCatalog,
    executionRepository,
    executionService,
    clock);

var failureSweep = await isolatedScheduler.DispatchDueAsync(100);
var failureDecisions = failureSweep.Decisions.ToArray();
var failedDecision = failureDecisions.Single(
    item => item.ConfiguredLogicId == failedDefinition.Id);
var healthyDecision = failureDecisions.Single(
    item => item.ConfiguredLogicId == healthyDefinition.Id);

if (failedDecision.Outcome != ScheduledShadowDispatchOutcome.DispatchFailed ||
    failedDecision.ReasonCode != "SCHEDULER_DISPATCH_FAILED" ||
    failedDecision.ExecutionId is not null ||
    failedDecision.ScheduledForUtc != expectedResumedOccurrence ||
    healthyDecision.Outcome != ScheduledShadowDispatchOutcome.Dispatched ||
    healthyDecision.ExecutionId is null ||
    Array.FindIndex(
        failureDecisions,
        item => item.ConfiguredLogicId == failedDefinition.Id) >=
    Array.FindIndex(
        failureDecisions,
        item => item.ConfiguredLogicId == healthyDefinition.Id))
{
    throw new InvalidOperationException(
        "Scheduler did not isolate a failed candidate while dispatching the next due candidate.");
}

var isolationOccurrence = expectedResumedOccurrence;
if (await executionRepository.ScheduledOccurrenceExistsAsync(
        failedRevisionId,
        isolationOccurrence) ||
    !await executionRepository.ScheduledOccurrenceExistsAsync(
        healthyRevisionId,
        isolationOccurrence))
{
    throw new InvalidOperationException(
        "Failed dispatch wrote an occurrence, or the healthy candidate was not persisted.");
}

// Retry the same clock interval. Only the previously failed occurrence may
// dispatch; the successful candidate must stay deduplicated.
faultInjectingCatalog.FailTargetGet = false;
var recoveredSweep = await isolatedScheduler.DispatchDueAsync(100);
var recoveredFailure = recoveredSweep.Decisions.Single(
    item => item.ConfiguredLogicId == failedDefinition.Id);
var recoveredHealthy = recoveredSweep.Decisions.Single(
    item => item.ConfiguredLogicId == healthyDefinition.Id);

if (recoveredFailure.Outcome != ScheduledShadowDispatchOutcome.Dispatched ||
    recoveredFailure.ExecutionId is null ||
    recoveredHealthy.Outcome != ScheduledShadowDispatchOutcome.AlreadyDispatched ||
    !await executionRepository.ScheduledOccurrenceExistsAsync(
        failedRevisionId,
        isolationOccurrence))
{
    throw new InvalidOperationException(
        "Scheduler retry did not recover the failed occurrence without duplicate dispatch.");
}

// Caller cancellation is not a per-candidate failure. The sweep must stop
// immediately and must not enqueue either next-interval occurrence.
clock.Advance(TimeSpan.FromMinutes(1));
var cancelledOccurrence = new DateTimeOffset(
    2026, 10, 8, 12, 17, 0, TimeSpan.Zero);

using (var cancellation = new CancellationTokenSource())
{
    faultInjectingCatalog.CancelTargetGet = cancellation;

    try
    {
        await isolatedScheduler.DispatchDueAsync(100, cancellation.Token);
        throw new InvalidOperationException(
            "Scheduler swallowed cancellation and continued the sweep.");
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
    {
        // Expected: cancellation must propagate to the hosting scheduler.
    }
    finally
    {
        faultInjectingCatalog.CancelTargetGet = null;
    }
}

if (await executionRepository.ScheduledOccurrenceExistsAsync(
        failedRevisionId,
        cancelledOccurrence) ||
    await executionRepository.ScheduledOccurrenceExistsAsync(
        healthyRevisionId,
        cancelledOccurrence))
{
    throw new InvalidOperationException(
        "Cancelled sweep persisted a scheduled occurrence.");
}

// Scheduled live reads act as a designated service principal. Without one the occurrence is blocked with a visible
// reason; with one the read plan is pinned against that principal's grants; deactivating it revokes the delegation.
{
    var liveWell = await assetService.CreateAssetAsync(
        new CreateAssetCommand(wellType.Id, "Scheduler Live Read Well", null, "{}"));
    var liveLogic = await configuredLogicService.CreateAsync(
        new CreateConfiguredLogicCommand(
            $"Scheduled live read {Guid.NewGuid():N}",
            manifest,
            """{"targetFillage":75}""",
            [new ConfiguredLogicAssetBindingCommand("well", liveWell.Id, "{}")],
            [new ConfiguredLogicDataBindingCommand("pump-fillage", liveWell.Id, "cygnet", "SITE::WELL", "{}")],
            new ConfiguredLogicScheduleCommand(true, 60, scheduleStart, "UTC")));
    var liveRevision = liveLogic.Revisions.Single();
    await configuredLogicService.ValidateRevisionAsync(liveLogic.Id, liveRevision.Id);
    await configuredLogicService.ActivateRevisionAsync(liveLogic.Id, liveRevision.Id);

    var withoutAuthority = (await scheduler.DispatchDueAsync(100)).Decisions
        .Single(item => item.ConfiguredLogicId == liveLogic.Id);
    var principalId = Guid.NewGuid();
    var identities = new FakeIdentityRepository(principalId);
    var authorizedScheduler = new ScheduledShadowSchedulerService(
        configuredLogicRepository, moduleRepository, executionRepository, executionService, clock,
        new ScheduledReadAuthorityProvider(new ScheduledReadAuthorityOptions { ServicePrincipalUserId = principalId },
            identities, new WellSiteAutoPilot.Persistence.Scheduling.DelegatedAuthorityAuditWriter(dbContext), clock));
    var withAuthority = (await authorizedScheduler.DispatchDueAsync(100)).Decisions
        .Single(item => item.ConfiguredLogicId == liveLogic.Id);
    if (withoutAuthority.ReasonCode != "EXECUTION_READ_AUTHORITY_REQUIRED" ||
        withAuthority.ReasonCode != "EXECUTION_READ_SOURCE_INVALID")
    {
        throw new InvalidOperationException("Scheduled live read did not require, then accept, a read authority.");
    }

    // Revocation: deactivating the service principal refuses the occurrence and is audited.
    identities.Active = false;
    var revoked = (await authorizedScheduler.DispatchDueAsync(100)).Decisions
        .Single(item => item.ConfiguredLogicId == liveLogic.Id);
    var refusedAudits = await dbContext.AuditEvents.CountAsync(item =>
        item.Action == "scheduler.delegation.refused" && item.TargetId == liveLogic.Id.ToString());
    if (revoked.ReasonCode != "SCHEDULER_READ_AUTHORITY_REVOKED" || refusedAudits < 1)
    {
        throw new InvalidOperationException("Revoked service principal was not refused and audited.");
    }
}

Console.WriteLine(
    "Scheduled Shadow due calculation, durable dispatch, idempotency, overlap blocking, and no-burst replay, candidate failure isolation, recovery, and cancellation checks passed.");
return 0;

file sealed class FakeIdentityRepository(Guid id) : IUserIdentityRepository
{
    public bool Active { get; set; } = true;

    public Task<WellSiteAutoPilot.Domain.Security.UserIdentity?> GetIdentityAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<WellSiteAutoPilot.Domain.Security.UserIdentity?>(userId == id
            ? new WellSiteAutoPilot.Domain.Security.UserIdentity(id, "svc", "svc", null, Active, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
            : null);

    public Task<IReadOnlyCollection<WellSiteAutoPilot.Domain.Security.UserIdentity>> ListIdentitiesAsync(int limit, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<WellSiteAutoPilot.Domain.Security.UserIdentity> UpsertIdentityAsync(string identityKey, string identityName,
        string normalizedIdentityName, string? displayName, DateTimeOffset seenAtUtc, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

file sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan amount)
    {
        _utcNow = _utcNow.Add(amount);
    }
}


file sealed class FaultInjectingModuleCatalogRepository(
    ILogicModuleCatalogRepository inner,
    string faultedModuleId) : ILogicModuleCatalogRepository
{
    public Task<InstalledLogicModule> RegisterAuthorizedAsync(InstalledLogicModule logicModule, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default) =>
        inner.RegisterAuthorizedAsync(logicModule, subject, actor, cancellationToken);

    public bool FailTargetGet { get; set; } = true;

    public CancellationTokenSource? CancelTargetGet { get; set; }

    public Task<InstalledLogicModule?> GetAsync(
        string moduleId,
        string version,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(moduleId, faultedModuleId, StringComparison.Ordinal))
        {
            if (CancelTargetGet is { } cancellationSource)
            {
                cancellationSource.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (FailTargetGet)
            {
                throw new InvalidOperationException(
                    "Injected catalog failure for scheduler isolation regression.");
            }
        }

        return inner.GetAsync(moduleId, version, cancellationToken);
    }

    public Task<IReadOnlyCollection<InstalledLogicModule>> ListAsync(
        string? moduleId,
        int limit,
        CancellationToken cancellationToken = default) =>
        inner.ListAsync(moduleId, limit, cancellationToken);

    public Task AddAsync(
        InstalledLogicModule module,
        CancellationToken cancellationToken = default) =>
        inner.AddAsync(module, cancellationToken);
}
