using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Recommendations;
using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Domain.Assets;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Control;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Domain.Recommendations;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Control;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Logic;
using WellSiteAutoPilot.Persistence.Recommendations;

// Execution modes: a Configured Logic revision carries one immutable mode that is copied onto every Execution it
// produces. The platform accepts the modes it can govern (Shadow, Recommendation, Autonomous) and every layer must agree
// on that set. Autonomous is scheduler-only: a person never runs it, and only the autonomy gate may turn its
// intents into ControlActions.

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
var controlRepository = new ControlRepository(db);
var simulator = new SimulatedControlCommandSink();
var gateOptions = new AutonomousControlOptions { Enabled = true };
var gate = new AutonomousControlService(controlRepository, recommendationRepository, executionRepository,
    configuredLogicRepository, [simulator, new RefusingProviderControlCommandSink()], gateOptions, clock);

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

// 2. Autonomous can be configured like any governed mode; undefined values cannot, neither as a new configuration
// nor as a new revision of an existing one.
{
    var autonomous = await ActiveConfigurationAsync(ExecutionMode.Autonomous, null);
    var storedAutonomous = await db.ConfiguredLogicRevisions.AsNoTracking().SingleAsync(item => item.Id == autonomous.Revision.Id);
    Require(storedAutonomous.Mode == nameof(ExecutionMode.Autonomous), "The Autonomous mode was not persisted on the revision.");
    var well = await NewWellAsync();
    var revised = await configuredLogicService.CreateRevisionAsync(shadow.Logic.Id,
        new CreateConfiguredLogicRevisionCommand(manifest, "{}", [new("well", shadow.Well.Id, "{}")],
            [new("pump-fillage", shadow.Well.Id, "simulator", "WELL-1", "{}")], null, Mode: ExecutionMode.Autonomous));
    Require(revised.Mode == ExecutionMode.Autonomous, "An Autonomous revision was not created with its mode.");
    await RequireValidationFailureAsync(async () => await configuredLogicService.CreateAsync(
        new CreateConfiguredLogicCommand($"Mode undefined {Guid.NewGuid():N}", manifest, "{}",
            [new("well", well.Id, "{}")], [new("pump-fillage", well.Id, "simulator", "WELL-1", "{}")], null,
            Mode: (ExecutionMode)99)),
        "An undefined mode value was accepted on a new configuration.");
    await RequireValidationFailureAsync(async () => await configuredLogicService.CreateRevisionAsync(shadow.Logic.Id,
        new CreateConfiguredLogicRevisionCommand(manifest, "{}", [new("well", shadow.Well.Id, "{}")],
            [new("pump-fillage", shadow.Well.Id, "simulator", "WELL-1", "{}")], null, Mode: (ExecutionMode)99)),
        "An undefined mode value was accepted on a new revision.");

    // A person never runs Autonomous: only the scheduler does, and its commands pass the gate.
    try
    {
        await configuredLogicService.PrepareManualShadowAsync(autonomous.Logic.Id, autonomous.Revision.Id);
        throw new InvalidOperationException("An Autonomous revision was prepared for a manual run.");
    }
    catch (WellSiteAutoPilotException exception) when (exception.Kind == FailureKind.Conflict)
    {
    }
}

// 3. A person may run Shadow and Recommendation revisions, and the prepared command carries the revision's mode.
{
    var prepared = await configuredLogicService.PrepareManualShadowAsync(recommendation.Logic.Id, recommendation.Revision.Id);
    Require(prepared.Mode == ExecutionMode.Recommendation, "A manual run lost the revision's Recommendation mode.");
    var preparedShadow = await configuredLogicService.PrepareManualShadowAsync(shadow.Logic.Id, shadow.Revision.Id);
    Require(preparedShadow.Mode == ExecutionMode.Shadow, "A manual run lost the revision's Shadow mode.");
}

// 4. Requesting an execution copies the mode onto the Execution; an undefined mode on a command is refused.
{
    var requested = await executionService.RequestConfiguredShadowAsync(
        await configuredLogicService.PrepareManualShadowAsync(recommendation.Logic.Id, recommendation.Revision.Id),
        $"mode-request-{Guid.NewGuid():N}");
    var persisted = await executionService.GetRequiredAsync(requested.Id);
    Require(requested.Mode == ExecutionMode.Recommendation && persisted.Mode == ExecutionMode.Recommendation,
        "The Execution did not carry the Recommendation mode.");

    var prepared = await configuredLogicService.PrepareManualShadowAsync(shadow.Logic.Id, shadow.Revision.Id);
    await RequireValidationFailureAsync(async () => await executionService.RequestConfiguredShadowAsync(
        prepared with { Mode = (ExecutionMode)99 }, $"mode-undefined-{Guid.NewGuid():N}"),
        "An Execution with an undefined mode was accepted.");
}

// 5. The scheduler dispatches every governed mode with its own mode. A mode the platform does not recognize is never
// dispatched, even if one is forced into the table: this proves the SQL candidate filter and the policy agree.
{
    var start = clock.GetUtcNow().AddHours(-1);
    var schedule = new ConfiguredLogicScheduleCommand(true, 60, start, "UTC");
    var scheduledShadow = await ActiveConfigurationAsync(ExecutionMode.Shadow, schedule);
    var scheduledRecommendation = await ActiveConfigurationAsync(ExecutionMode.Recommendation, schedule);
    var scheduledAutonomous = await ActiveConfigurationAsync(ExecutionMode.Autonomous, schedule);
    var forced = await ActiveConfigurationAsync(ExecutionMode.Shadow, schedule);
    await db.ConfiguredLogicRevisions.Where(item => item.Id == forced.Revision.Id)
        .ExecuteUpdateAsync(set => set.SetProperty(item => item.Mode, "Unrecognized"));

    var sweep = await scheduler.DispatchDueAsync(500);
    async Task<ExecutionMode> DispatchedModeAsync(Guid configuredLogicId, string label)
    {
        var decision = sweep.Decisions.Single(item => item.ConfiguredLogicId == configuredLogicId);
        Require(decision.Outcome == ScheduledShadowDispatchOutcome.Dispatched, $"The scheduler did not dispatch the {label} revision.");
        return (await executionService.GetRequiredAsync(decision.ExecutionId!.Value)).Mode;
    }

    Require(await DispatchedModeAsync(scheduledShadow.Logic.Id, "Shadow") == ExecutionMode.Shadow, "A scheduled Shadow run did not stay Shadow.");
    Require(await DispatchedModeAsync(scheduledRecommendation.Logic.Id, "Recommendation") == ExecutionMode.Recommendation,
        "A scheduled Recommendation run did not carry its mode.");
    Require(await DispatchedModeAsync(scheduledAutonomous.Logic.Id, "Autonomous") == ExecutionMode.Autonomous,
        "A scheduled Autonomous run did not carry its mode.");
    Require(!sweep.Decisions.Any(item => item.ConfiguredLogicId == forced.Logic.Id &&
                                         item.Outcome == ScheduledShadowDispatchOutcome.Dispatched),
        "The scheduler dispatched a revision with an unrecognized mode.");
    Require(!await db.Executions.AnyAsync(item => item.LogicInstanceId == forced.Logic.Id),
        "A revision with an unrecognized mode produced an Execution.");
}

// 6. Recommendation and Autonomous executions materialize their intents (the latter only to be decided by the gate);
// a Shadow run observes its intents only.
{
    async Task<int> MaterializeAsync(ExecutionMode mode)
    {
        // A person cannot prepare an Autonomous run, so prepare it as Shadow and switch both the revision and the command,
        // which is exactly the pair of facts the scheduler produces.
        var active = await ActiveConfigurationAsync(mode == ExecutionMode.Autonomous ? ExecutionMode.Shadow : mode, null);
        var command = await configuredLogicService.PrepareManualShadowAsync(active.Logic.Id, active.Revision.Id);
        if (mode == ExecutionMode.Autonomous)
        {
            await db.ConfiguredLogicRevisions.Where(item => item.Id == active.Revision.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.Mode, nameof(ExecutionMode.Autonomous)));
            command = command with { Mode = ExecutionMode.Autonomous };
        }
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
    Require(await MaterializeAsync(ExecutionMode.Autonomous) == 1, "An Autonomous execution did not produce its intent.");
}

// 6b. The autonomy gate. These executions are built directly: the revision is prepared while a person can still run it,
// then switched to Autonomous, so the execution row is exactly what the scheduler would have produced.
{
    var limits = new[] { new AutonomyCommandLimit("SetSpeed", "spm", 5m, 12m) };

    async Task<(Guid ConfiguredLogicId, Guid ExecutionId)> AutonomousExecutionAsync(params decimal[] speeds)
    {
        var active = await ActiveConfigurationAsync(ExecutionMode.Shadow, null);
        // Prepare the request while the revision is still runnable by a person, then force the mode.
        var command = await configuredLogicService.PrepareManualShadowAsync(active.Logic.Id, active.Revision.Id);
        await db.ConfiguredLogicRevisions.Where(item => item.Id == active.Revision.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.Mode, nameof(ExecutionMode.Autonomous)));
        var execution = new ExecutionRecord(Guid.NewGuid(), active.Logic.Id, active.Revision.ModuleId, active.Revision.ModuleVersion,
            active.Revision.Id, null, null, null, ExecutionMode.Autonomous, ExecutionStatus.Requested,
            $"gate-{Guid.NewGuid():N}", clock.GetUtcNow(), 2, ExecutionTriggerKind.Manual);
        await executionRepository.AddConfiguredRequestedAsync(execution, command, null);
        var output = JsonSerializer.Serialize(new LogicModuleExecutionResult("GATE_TEST_COMPLETED", [], [], [],
            new Dictionary<string, string>()), JsonSerializerOptions.Web);
        await executionRepository.ApplyCompletedAsync(Guid.NewGuid(), $"gate-result-{Guid.NewGuid():N}", execution.Id,
            clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(1), "GATE_TEST_COMPLETED", output);
        var records = speeds.Select((speed, index) => new RecommendationRecord(Guid.NewGuid(), execution.Id, index,
            active.Logic.Id, active.Revision.Id, active.Revision.ModuleId, active.Revision.ModuleVersion, active.Well.Id,
            "SET_PUMPING_SPEED", "SetSpeed", "PumpingSpeed", speed, "spm", "OPTIMIZED", "{}",
            RecommendationStatus.Created, clock.GetUtcNow())).ToArray();
        Require(await recommendationRepository.AddMissingAsync(records) == records.Length, "Gate test intents were not stored.");
        return (active.Logic.Id, execution.Id);
    }

    // Only completed Autonomous executions reach the gate: Shadow and Recommendation never do.
    var notAutonomous = await executionService.RequestConfiguredShadowAsync(
        await configuredLogicService.PrepareManualShadowAsync(recommendation.Logic.Id, recommendation.Revision.Id),
        $"gate-skip-{Guid.NewGuid():N}");
    Require(await gate.ProcessExecutionAsync(notAutonomous.Id) == 0, "A non-Autonomous execution reached the control gate.");

    // No policy: every intent is blocked, nothing is sent.
    var first = await AutonomousExecutionAsync(9m, 50m, 10m, 11m);
    Require(await gate.ProcessExecutionAsync(first.ExecutionId) == 4 && simulator.Sent.Count == 0,
        "Autonomous commands were dispatched without a policy.");
    var blocked = await controlRepository.ListActionsAsync(first.ConfiguredLogicId, 50);
    Require(blocked.Count == 4 && blocked.All(item => item.Status == ControlActionStatus.Blocked && item.GateCode == GateCodes.NoPolicy),
        "A missing policy must block every intent with GATE_NO_POLICY.");
    Require(!await recommendationRepository.TryDecideAsync(
            (await recommendationRepository.ListByExecutionAsync(first.ExecutionId)).First().Id,
            RecommendationStatus.Approved, "operator", "x", clock.GetUtcNow()),
        "A blocked Autonomous recommendation could still be approved by a person.");

    // Real provider writes cannot even be configured.
    try
    {
        await gate.SetPolicyAsync(first.ConfiguredLogicId, true, ControlSinkKinds.Provider, limits, 1, 60, "admin");
        throw new InvalidOperationException("A Provider sink policy was accepted.");
    }
    catch (WellSiteAutoPilotException)
    {
    }

    // Bounds, rate limit and idempotency.
    var gated = await AutonomousExecutionAsync(9m, 50m, 10m, 11m);
    await gate.SetPolicyAsync(gated.ConfiguredLogicId, true, ControlSinkKinds.Simulator, limits, 2, 3600, "admin");
    await gate.ProcessExecutionAsync(gated.ExecutionId);
    await gate.ProcessExecutionAsync(gated.ExecutionId);
    var actions = await controlRepository.ListActionsAsync(gated.ConfiguredLogicId, 50);
    Require(actions.Count == 4, "The gate must record exactly one action per intent, even when processed twice.");
    Require(actions.Count(item => item.Status == ControlActionStatus.Dispatched) == 2 && simulator.Sent.Count == 2,
        "Exactly the two in-bounds intents within the rate limit should dispatch.");
    Require(actions.Single(item => item.Value == 50m).GateCode == GateCodes.OutOfBounds, "An out-of-bounds intent was not blocked.");
    Require(actions.Single(item => item.Value == 11m).GateCode == GateCodes.RateLimited, "The rate limit did not block the third command.");
    Require(simulator.Sent.All(item => item.Value is 9m or 10m), "An unexpected command reached the sink.");
    Require((await recommendationRepository.ListByExecutionAsync(gated.ExecutionId))
            .Count(item => item.Status == RecommendationStatus.Executed && item.ControlActionId is not null) == 2,
        "Dispatched recommendations must be Executed and linked to their ControlAction.");
    Require(await db.AuditEvents.CountAsync(item => item.TargetType == "control-action" && item.Action == "control.action.dispatched") >= 2,
        "Dispatch was not audited.");

    // The kill switch and the deployment switch each halt everything, even with a valid policy.
    var sentBefore = simulator.Sent.Count;
    try
    {
        await gate.SetKillSwitchAsync(true, "operator", "test");
        var halted = await AutonomousExecutionAsync(9m);
        await gate.SetPolicyAsync(halted.ConfiguredLogicId, true, ControlSinkKinds.Simulator, limits, 10, 3600, "admin");
        await gate.ProcessExecutionAsync(halted.ExecutionId);
        Require(simulator.Sent.Count == sentBefore &&
                (await controlRepository.ListActionsAsync(halted.ConfiguredLogicId, 10)).Single().GateCode == GateCodes.KillSwitch,
            "The kill switch did not halt autonomous dispatch.");
    }
    finally
    {
        await gate.SetKillSwitchAsync(false, "operator", "test over");
    }

    gateOptions.Enabled = false;
    var globallyOff = await AutonomousExecutionAsync(9m);
    await gate.SetPolicyAsync(globallyOff.ConfiguredLogicId, true, ControlSinkKinds.Simulator, limits, 10, 3600, "admin");
    await gate.ProcessExecutionAsync(globallyOff.ExecutionId);
    Require(simulator.Sent.Count == sentBefore &&
            (await controlRepository.ListActionsAsync(globallyOff.ConfiguredLogicId, 10)).Single().GateCode == GateCodes.GlobalDisabled,
        "The disabled deployment switch did not block dispatch.");
    gateOptions.Enabled = true;
}

// 7. The policy itself: undefined values are unsupported, by value and by name, and every predicate agrees.
foreach (var mode in new[] { ExecutionMode.Shadow, ExecutionMode.Recommendation, ExecutionMode.Autonomous })
    Require(ExecutionModePolicy.IsSupported(mode), $"{mode} must be supported.");
Require(!ExecutionModePolicy.IsSupported((ExecutionMode)99) && !ExecutionModePolicy.IsSupported("Unrecognized") &&
        !ExecutionModePolicy.IsSupported((string?)null),
    "Undefined modes must be unsupported.");
foreach (var mode in Enum.GetValues<ExecutionMode>())
{
    Require(ExecutionModePolicy.IsSupported(mode) == ExecutionModePolicy.IsSupported(mode.ToString()),
        $"The mode policy disagrees with itself by name for {mode}.");
    // Autonomous is the only supported mode a person may not run, and the only one besides Recommendation that yields intents.
    Require(ExecutionModePolicy.AllowsManualRun(mode) == (mode is ExecutionMode.Shadow or ExecutionMode.Recommendation),
        $"The manual-run rule is wrong for {mode}.");
    Require(ExecutionModePolicy.MaterializesControlIntents(mode) == (mode is ExecutionMode.Recommendation or ExecutionMode.Autonomous),
        $"The intent-materialization rule is wrong for {mode}.");
    Require(!ExecutionModePolicy.AllowsManualRun(mode) || ExecutionModePolicy.IsSupported(mode),
        $"{mode} may be run manually without being supported.");
}

Console.WriteLine("Execution mode safety tests passed.");
return 0;

file sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow.ToUniversalTime();
}
