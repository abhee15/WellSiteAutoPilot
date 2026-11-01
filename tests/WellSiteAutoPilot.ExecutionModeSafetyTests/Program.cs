using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Recommendations;
using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Domain.Assets;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Logic;
using WellSiteAutoPilot.Persistence.Recommendations;

// Execution modes: a Configured Logic revision carries one immutable mode that is copied onto every Execution it
// produces. The platform accepts only the modes it can govern (Shadow, Recommendation); Autonomous is refused until the
// control gate exists, and every layer must agree on that set.

var connectionString = Environment.GetEnvironmentVariable("WSA_DATABASE_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("WSA_DATABASE_CONNECTION_STRING is required.");

var dbOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>().UseNpgsql(connectionString).Options;
await using var db = new WellSiteAutoPilotDbContext(dbOptions);
var clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 8, 6, 0, 0, TimeSpan.Zero));

var assetRepository = new AssetRepository(db);
var configuredLogicRepository = new ConfiguredLogicRepository(db);
var executionRepository = new ExecutionRepository(db);
var moduleRepository = new LogicModuleCatalogRepository(db);
var recommendationRepository = new RecommendationRepository(db);

var assetService = new AssetService(assetRepository, clock);
var moduleCatalog = new LogicModuleCatalogService(moduleRepository, clock);
var configuredLogicService = new ConfiguredLogicService(configuredLogicRepository, assetRepository, moduleRepository, clock);
var executionService = new ExecutionService(executionRepository, clock);
var scheduler = new ScheduledShadowSchedulerService(configuredLogicRepository, moduleRepository, executionRepository,
    executionService, clock);
var materializer = new RecommendationMaterializer(executionRepository, recommendationRepository, clock);

var suffix = Guid.NewGuid().ToString("N");
var wellType = await assetService.CreateAssetTypeAsync(new CreateAssetTypeCommand(
    $"mode-well-{suffix}", "Mode Well", """{"type":"object"}"""));

var manifest = $$"""
{
  "manifestVersion": 1, "moduleId": "weatherford.mode-safety-{{suffix}}", "version": "1.0.0",
  "displayName": "Mode Safety", "publisher": "Weatherford", "runtime": "DotNet",
  "executionProfile": "ScheduledOneShot", "entryPoint": "Weatherford.ModeSafety.EntryPoint",
  "minimumSdkVersion": "1.0.0",
  "assetRequirements": [{ "role": "well", "minimumCount": 1, "maximumCount": 1,
    "requiredAssetTypeKey": "{{wellType.Key}}", "requiredTraits": [] }],
  "dataRequirements": [{ "id": "pump-fillage", "assetRole": "well", "quantity": "PumpFillage",
    "access": "Current", "canonicalUnit": "%", "maximumAgeSeconds": 60, "allowUncertainQuality": false }],
  "commandRequirements": [{ "id": "set-speed", "assetRole": "well", "command": "SetSpeed",
    "quantity": "PumpingSpeed", "canonicalUnit": "spm" }]
}
""";
await moduleCatalog.RegisterAsync(new RegisterLogicModuleCommand(manifest, new string('c', 64), LogicModuleTrustStatus.TrustedPublisher));

// Overlap rules block two pending runs on one Asset, so every scenario gets its own well.
var wellCounter = 0;
async Task<Asset> NewWellAsync() => await assetService.CreateAssetAsync(
    new CreateAssetCommand(wellType.Id, $"Mode Well {suffix} {++wellCounter}", null, "{}"));

async Task<(ConfiguredLogicDefinition Logic, ConfiguredLogicRevision Revision, Asset Well)> ActiveConfigurationAsync(
    ExecutionMode? mode, ConfiguredLogicScheduleCommand? schedule)
{
    var well = await NewWellAsync();
    IReadOnlyCollection<ConfiguredLogicAssetBindingCommand> assets = [new("well", well.Id, "{}")];
    IReadOnlyCollection<ConfiguredLogicDataBindingCommand> data = [new("pump-fillage", well.Id, "simulator", "WELL-1", "{}")];
    var logic = await configuredLogicService.CreateAsync(mode is { } explicitMode
        ? new CreateConfiguredLogicCommand($"Mode {mode} {Guid.NewGuid():N}", manifest, "{}", assets, data, schedule, Mode: explicitMode)
        : new CreateConfiguredLogicCommand($"Mode default {Guid.NewGuid():N}", manifest, "{}", assets, data, schedule));
    var revision = logic.Revisions.Single();
    await configuredLogicService.ValidateRevisionAsync(logic.Id, revision.Id);
    await configuredLogicService.ActivateRevisionAsync(logic.Id, revision.Id);
    return (logic, revision, well);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static async Task RequireValidationFailureAsync(Func<Task> action, string message)
{
    try
    {
        await action();
    }
    catch (WellSiteAutoPilotException exception) when (exception.Kind == FailureKind.Validation)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

// 1. The default is Shadow; an explicit Recommendation is persisted on the revision.
var shadow = await ActiveConfigurationAsync(null, null);
Require(shadow.Revision.Mode == ExecutionMode.Shadow, "A configuration without a mode must default to Shadow.");
var recommendation = await ActiveConfigurationAsync(ExecutionMode.Recommendation, null);
var stored = await db.ConfiguredLogicRevisions.AsNoTracking().SingleAsync(item => item.Id == recommendation.Revision.Id);
Require(stored.Mode == nameof(ExecutionMode.Recommendation), "The Recommendation mode was not persisted on the revision.");

// 2. Autonomous cannot be created, neither as a new configuration nor as a new revision of an existing one.
{
    var well = await NewWellAsync();
    await RequireValidationFailureAsync(async () => await configuredLogicService.CreateAsync(
        new CreateConfiguredLogicCommand($"Mode refused {Guid.NewGuid():N}", manifest, "{}",
            [new("well", well.Id, "{}")], [new("pump-fillage", well.Id, "simulator", "WELL-1", "{}")], null,
            Mode: ExecutionMode.Autonomous)),
        "An Autonomous configuration was accepted before the control gate exists.");
    await RequireValidationFailureAsync(async () => await configuredLogicService.CreateRevisionAsync(shadow.Logic.Id,
        new CreateConfiguredLogicRevisionCommand(manifest, "{}", [new("well", shadow.Well.Id, "{}")],
            [new("pump-fillage", shadow.Well.Id, "simulator", "WELL-1", "{}")], null, Mode: ExecutionMode.Autonomous)),
        "An Autonomous revision was accepted before the control gate exists.");
    await RequireValidationFailureAsync(async () => await configuredLogicService.CreateAsync(
        new CreateConfiguredLogicCommand($"Mode undefined {Guid.NewGuid():N}", manifest, "{}",
            [new("well", well.Id, "{}")], [new("pump-fillage", well.Id, "simulator", "WELL-1", "{}")], null,
            Mode: (ExecutionMode)99)),
        "An undefined mode value was accepted.");
}

// 3. A person may run Shadow and Recommendation revisions, and the prepared command carries the revision's mode.
{
    var prepared = await configuredLogicService.PrepareManualShadowAsync(recommendation.Logic.Id, recommendation.Revision.Id);
    Require(prepared.Mode == ExecutionMode.Recommendation, "A manual run lost the revision's Recommendation mode.");
    var preparedShadow = await configuredLogicService.PrepareManualShadowAsync(shadow.Logic.Id, shadow.Revision.Id);
    Require(preparedShadow.Mode == ExecutionMode.Shadow, "A manual run lost the revision's Shadow mode.");
}

// 4. Requesting an execution copies the mode onto the Execution; an unsupported mode on a command is refused.
{
    var requested = await executionService.RequestConfiguredShadowAsync(
        await configuredLogicService.PrepareManualShadowAsync(recommendation.Logic.Id, recommendation.Revision.Id),
        $"mode-request-{Guid.NewGuid():N}");
    var persisted = await executionService.GetRequiredAsync(requested.Id);
    Require(requested.Mode == ExecutionMode.Recommendation && persisted.Mode == ExecutionMode.Recommendation,
        "The Execution did not carry the Recommendation mode.");

    var prepared = await configuredLogicService.PrepareManualShadowAsync(shadow.Logic.Id, shadow.Revision.Id);
    await RequireValidationFailureAsync(async () => await executionService.RequestConfiguredShadowAsync(
        prepared with { Mode = ExecutionMode.Autonomous }, $"mode-autonomous-{Guid.NewGuid():N}"),
        "An Autonomous Execution was accepted before the control gate exists.");
}

// 5. The scheduler dispatches Shadow and Recommendation revisions with their own mode, and never an Autonomous one,
// even if one is forced into the table. The forced case also proves the SQL candidate filter and the policy agree.
{
    var start = clock.GetUtcNow().AddHours(-1);
    var schedule = new ConfiguredLogicScheduleCommand(true, 60, start, "UTC");
    var scheduledShadow = await ActiveConfigurationAsync(ExecutionMode.Shadow, schedule);
    var scheduledRecommendation = await ActiveConfigurationAsync(ExecutionMode.Recommendation, schedule);
    var forced = await ActiveConfigurationAsync(ExecutionMode.Shadow, schedule);
    await db.ConfiguredLogicRevisions.Where(item => item.Id == forced.Revision.Id)
        .ExecuteUpdateAsync(set => set.SetProperty(item => item.Mode, nameof(ExecutionMode.Autonomous)));

    var sweep = await scheduler.DispatchDueAsync(1000);
    var shadowDecision = sweep.Decisions.Single(item => item.ConfiguredLogicId == scheduledShadow.Logic.Id);
    var recommendationDecision = sweep.Decisions.Single(item => item.ConfiguredLogicId == scheduledRecommendation.Logic.Id);
    Require(shadowDecision.Outcome == ScheduledShadowDispatchOutcome.Dispatched &&
            recommendationDecision.Outcome == ScheduledShadowDispatchOutcome.Dispatched,
        "The scheduler did not dispatch both supported modes.");
    Require((await executionService.GetRequiredAsync(shadowDecision.ExecutionId!.Value)).Mode == ExecutionMode.Shadow,
        "A scheduled Shadow run did not stay Shadow.");
    Require((await executionService.GetRequiredAsync(recommendationDecision.ExecutionId!.Value)).Mode == ExecutionMode.Recommendation,
        "A scheduled Recommendation run did not carry its mode.");
    Require(!sweep.Decisions.Any(item => item.ConfiguredLogicId == forced.Logic.Id &&
                                         item.Outcome == ScheduledShadowDispatchOutcome.Dispatched),
        "The scheduler dispatched an Autonomous revision.");
    Require(!await db.Executions.AnyAsync(item => item.LogicInstanceId == forced.Logic.Id),
        "An Autonomous revision produced an Execution.");
}

// 6. Only Recommendation executions become operator recommendations; a Shadow run observes its intents only.
{
    async Task<int> MaterializeAsync(ExecutionMode mode)
    {
        var active = await ActiveConfigurationAsync(mode, null);
        var command = (await configuredLogicService.PrepareManualShadowAsync(active.Logic.Id, active.Revision.Id));
        var execution = await executionService.RequestConfiguredShadowAsync(command, $"mode-materialize-{Guid.NewGuid():N}");
        var result = new LogicModuleExecutionResult("MODE_TEST_COMPLETED", [], [],
            [new ControlIntentDraft("SET_PUMPING_SPEED", active.Well.Id, "SetSpeed", "PumpingSpeed", 9m, "spm", "OPTIMIZED")],
            new Dictionary<string, string>());
        var output = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
        await executionRepository.ApplyCompletedAsync(Guid.NewGuid(), $"mode-result-{Guid.NewGuid():N}", execution.Id,
            clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(1), result.OutcomeCode, output);
        return await materializer.MaterializeAsync(execution.Id, output);
    }

    Require(await MaterializeAsync(ExecutionMode.Shadow) == 0, "A Shadow execution produced a recommendation.");
    Require(await MaterializeAsync(ExecutionMode.Recommendation) == 1, "A Recommendation execution did not produce its recommendation.");
}

// 7. The policy itself: Autonomous and undefined values are unsupported, by value and by name.
Require(ExecutionModePolicy.IsSupported(ExecutionMode.Shadow) && ExecutionModePolicy.IsSupported(ExecutionMode.Recommendation),
    "Shadow and Recommendation must be supported.");
Require(!ExecutionModePolicy.IsSupported(ExecutionMode.Autonomous) && !ExecutionModePolicy.IsSupported((ExecutionMode)99),
    "Autonomous and undefined modes must be unsupported.");
foreach (var mode in Enum.GetValues<ExecutionMode>())
    Require(ExecutionModePolicy.IsSupported(mode) == ExecutionModePolicy.IsSupported(mode.ToString()) &&
            ExecutionModePolicy.AllowsManualRun(mode) == ExecutionModePolicy.IsSupported(mode),
        $"The mode policy disagrees with itself for {mode}.");

Console.WriteLine("Execution mode safety tests passed.");
return 0;

file sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow.ToUniversalTime();
}
