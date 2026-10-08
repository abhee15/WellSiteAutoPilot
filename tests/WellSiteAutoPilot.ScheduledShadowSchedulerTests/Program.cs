using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Scheduling;
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

Console.WriteLine(
    "Scheduled Shadow due calculation, durable dispatch, idempotency, overlap blocking, and no-burst replay checks passed.");
return 0;

file sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan amount)
    {
        _utcNow = _utcNow.Add(amount);
    }
}
