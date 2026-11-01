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
var scheduler = new ScheduledShadowSchedulerService(configuredLogicRepository, new ConfiguredLogicScheduleRepository(db), moduleRepository, executionRepository,
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
    async Task<(int Count, Guid ExecutionId)> MaterializeAsync(ExecutionMode mode, string? controlCommand = "SetSpeed")
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
        var result = new LogicModuleExecutionResult("MODE_TEST_COMPLETED", [],
            [new RecommendationIntentDraft("REVIEW_SPEED", active.Well.Id, "Review pump speed", "Fillage is trending down.",
                "PumpingSpeed", 8m, "spm")],
            controlCommand is null
                ? []
                : [new ControlIntentDraft("SET_PUMPING_SPEED", active.Well.Id, controlCommand, "PumpingSpeed", 9m, "spm", "OPTIMIZED")],
            new Dictionary<string, string>());
        var output = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
        await executionRepository.ApplyCompletedAsync(Guid.NewGuid(), $"mode-result-{Guid.NewGuid():N}", execution.Id,
            clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(1), result.OutcomeCode, output);
        return (await materializer.MaterializeAsync(execution.Id, output), execution.Id);
    }

    Require((await MaterializeAsync(ExecutionMode.Shadow)).Count == 0, "A Shadow execution produced a recommendation.");
    // Advice is proposed to a person, so only Recommendation mode records it; the control intent is recorded alongside.
    var proposed = await MaterializeAsync(ExecutionMode.Recommendation);
    var proposedRecords = await recommendationRepository.ListByExecutionAsync(proposed.ExecutionId);
    Require(proposed.Count == 2 && proposedRecords.Count == 2 &&
            proposedRecords.Single(item => item.Command == "SetSpeed").IntentIndex == 0 &&
            proposedRecords.Single(item => item.Command == RecommendationCommands.Advisory) is
            { Code: "REVIEW_SPEED", IntentIndex: 1, SuggestedValue: 8m, Unit: "spm", Status: RecommendationStatus.Created },
        "A Recommendation execution did not produce its control and advisory recommendations.");
    var autonomous = await MaterializeAsync(ExecutionMode.Autonomous);
    Require(autonomous.Count == 1 && (await recommendationRepository.ListByExecutionAsync(autonomous.ExecutionId))
            .All(item => item.Command != RecommendationCommands.Advisory),
        "An Autonomous execution must materialize its control intent only, never advice.");
    // The advisory command is reserved: a module cannot pass a control command off as advice.
    try
    {
        await MaterializeAsync(ExecutionMode.Recommendation, RecommendationCommands.Advisory);
        throw new InvalidOperationException("A control intent used the reserved advisory command.");
    }
    catch (WellSiteAutoPilotException exception) when (exception.Kind == FailureKind.Validation)
    {
    }
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
    Require(actions.Count(item => item.Status == ControlActionStatus.Verified) == 2 && simulator.Sent.Count == 2,
        "Exactly the two in-bounds intents within the rate limit should dispatch and read back as applied.");
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

// 6c. A person's approval of a Recommendation-mode recommendation can be executed through the same gate, but only
// when the configuration opted in, and every other gate check still applies.
{
    var limits = new[] { new AutonomyCommandLimit("SetSpeed", "spm", 5m, 12m) };

    async Task<Guid> ApprovedRecommendationAsync(
        (ConfiguredLogicDefinition Logic, ConfiguredLogicRevision Revision, Asset Well) active, decimal speed, bool approve = true,
        bool advisory = false)
    {
        var command = await configuredLogicService.PrepareManualShadowAsync(active.Logic.Id, active.Revision.Id);
        var execution = await executionService.RequestConfiguredShadowAsync(command, $"approved-{Guid.NewGuid():N}");
        var result = new LogicModuleExecutionResult("APPROVED_TEST_COMPLETED", [],
            advisory ? [new RecommendationIntentDraft("REVIEW_SPEED", active.Well.Id, "Review pump speed", "Advice.", "PumpingSpeed", speed, "spm")] : [],
            advisory ? [] : [new ControlIntentDraft("SET_PUMPING_SPEED", active.Well.Id, "SetSpeed", "PumpingSpeed", speed, "spm", "OPTIMIZED")],
            new Dictionary<string, string>());
        var output = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
        await executionRepository.ApplyCompletedAsync(Guid.NewGuid(), $"approved-result-{Guid.NewGuid():N}", execution.Id,
            clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(1), result.OutcomeCode, output);
        Require(await materializer.MaterializeAsync(execution.Id, output) == 1, "The approved-path intent was not materialized.");
        var id = (await recommendationRepository.ListByExecutionAsync(execution.Id)).Single().Id;
        if (approve)
            Require(await recommendationRepository.TryDecideAsync(id, RecommendationStatus.Approved, "operator", "ok", clock.GetUtcNow()),
                "A person could not approve a Recommendation-mode recommendation.");
        return id;
    }

    async Task<RecommendationStatus> StatusAsync(Guid id) => (await recommendationRepository.GetAsync(id))!.Status;
    async Task<bool> RefusalAuditedAsync(Guid id, string code) => await db.AuditEvents.AnyAsync(item =>
        item.TargetType == "recommendation" && item.TargetId == id.ToString() && item.Action == "control.action.refused" &&
        item.DetailsJson.Contains(code));

    // Not yet approved: nothing to execute.
    var logic = await ActiveConfigurationAsync(ExecutionMode.Recommendation, null);
    var pending = await ApprovedRecommendationAsync(logic, 9m, approve: false);
    try
    {
        await gate.ExecuteApprovedAsync(pending, "operator");
        throw new InvalidOperationException("An unapproved recommendation was executed.");
    }
    catch (WellSiteAutoPilotException exception) when (exception.Kind == FailureKind.Conflict)
    {
    }

    // The gate's own decisions are not a person's approval, so a blocked Autonomous recommendation is not executable.
    var autonomousBlocked = (await db.Recommendations.AsNoTracking()
        .Where(item => item.DecisionBy == "system:autonomy-gate").Select(item => item.Id).FirstAsync());
    try
    {
        await gate.ExecuteApprovedAsync(autonomousBlocked, "operator");
        throw new InvalidOperationException("A gate decision was executed as an approval.");
    }
    catch (WellSiteAutoPilotException exception) when (exception.Kind == FailureKind.Conflict)
    {
    }

    // Approved, but every gate check still decides; a refusal changes nothing except the audit trail.
    var approved = await ApprovedRecommendationAsync(logic, 9m);
    var sentBefore = simulator.Sent.Count;
    async Task RequireRefusedAsync(Guid id, string code, string why)
    {
        var refused = await gate.ExecuteApprovedAsync(id, "operator");
        Require(refused.Action is null && refused.RefusalCode == code, $"{why}: expected {code}, got {refused.RefusalCode}.");
        Require(await StatusAsync(id) == RecommendationStatus.Approved && await RefusalAuditedAsync(id, code) &&
                await controlRepository.GetActionByRecommendationAsync(id) is null && simulator.Sent.Count == sentBefore,
            $"{why}: a refusal must leave the recommendation Approved, audit it and send nothing.");
    }

    await RequireRefusedAsync(approved, GateCodes.NoPolicy, "No policy");
    await gate.SetPolicyAsync(logic.Logic.Id, true, ControlSinkKinds.Simulator, limits, 1, 3600, "admin");
    await RequireRefusedAsync(approved, GateCodes.ApprovedNotEnabled, "Autonomy policy without approved-execution opt-in");
    await gate.SetPolicyAsync(logic.Logic.Id, true, ControlSinkKinds.Simulator, limits, 1, 3600, "admin",
        allowApprovedRecommendations: true);
    try
    {
        await gate.SetKillSwitchAsync(true, "operator", "test");
        await RequireRefusedAsync(approved, GateCodes.KillSwitch, "Kill switch");
    }
    finally
    {
        await gate.SetKillSwitchAsync(false, "operator", "test over");
    }

    gateOptions.Enabled = false;
    await RequireRefusedAsync(approved, GateCodes.GlobalDisabled, "Deployment switch");
    gateOptions.Enabled = true;

    var outOfBounds = await ApprovedRecommendationAsync(logic, 50m);
    await RequireRefusedAsync(outOfBounds, GateCodes.OutOfBounds, "Out-of-bounds value");

    // An approved recommendation older than the staleness limit describes a plant that has moved on.
    var staleLimit = gateOptions.ApprovedRecommendationMaxAge;
    clock.Advance(staleLimit + TimeSpan.FromMinutes(1));
    try
    {
        await RequireRefusedAsync(approved, GateCodes.Stale, "Stale approval");
    }
    finally
    {
        clock.Advance(-(staleLimit + TimeSpan.FromMinutes(1)));
    }

    // Everything holds: it dispatches once, is attributed to the approval, and a repeat is a no-op.
    var executed = await gate.ExecuteApprovedAsync(approved, "operator");
    Require(executed.RefusalCode is null && executed.Action is { Status: ControlActionStatus.Verified, GateCode: GateCodes.ReadbackConfirmed, Origin: ControlOrigins.ApprovedRecommendation },
        "An approved recommendation inside its policy did not dispatch and verify.");
    Require(simulator.Sent.Count == sentBefore + 1 && await StatusAsync(approved) == RecommendationStatus.Executed,
        "Dispatch must send exactly once and mark the recommendation Executed.");
    var repeated = await gate.ExecuteApprovedAsync(approved, "operator");
    Require(repeated.Action?.Id == executed.Action!.Id && simulator.Sent.Count == sentBefore + 1,
        "Executing the same recommendation twice must not send twice.");

    // The window allows one action: a second approved recommendation is refused, not queued.
    var second = await ApprovedRecommendationAsync(logic, 10m);
    var limited = await gate.ExecuteApprovedAsync(second, "operator");
    Require(limited.RefusalCode == GateCodes.RateLimited && await StatusAsync(second) == RecommendationStatus.Approved,
        "The rate limit did not apply to approved execution.");

    // A revision change after approval invalidates it.
    var superseded = await ActiveConfigurationAsync(ExecutionMode.Recommendation, null);
    await gate.SetPolicyAsync(superseded.Logic.Id, true, ControlSinkKinds.Simulator, limits, 5, 3600, "admin",
        allowApprovedRecommendations: true);
    var supersededId = await ApprovedRecommendationAsync(superseded, 9m);
    await db.ConfiguredLogicRevisions.Where(item => item.Id == superseded.Revision.Id)
        .ExecuteUpdateAsync(set => set.SetProperty(item => item.Mode, nameof(ExecutionMode.Shadow)));
    // Advice has no device action: approved by a person inside an opted-in configuration it is still refused, and no
    // policy can permit its command.
    var advice = await ApprovedRecommendationAsync(logic, 9m, advisory: true);
    var adviceSent = simulator.Sent.Count;
    var adviceResult = await gate.ExecuteApprovedAsync(advice, "operator");
    Require(adviceResult.Action is null && adviceResult.RefusalCode == GateCodes.AdvisoryNotExecutable &&
            await StatusAsync(advice) == RecommendationStatus.Approved && simulator.Sent.Count == adviceSent,
        "An approved advisory recommendation must be refused and send nothing.");
    try
    {
        await gate.SetPolicyAsync(logic.Logic.Id, true, ControlSinkKinds.Simulator,
            [new AutonomyCommandLimit(RecommendationCommands.Advisory, "spm", 1m, 2m)], 5, 3600, "admin",
            allowApprovedRecommendations: true);
        throw new InvalidOperationException("A policy permitted the reserved advisory command.");
    }
    catch (WellSiteAutoPilotException exception) when (exception.Kind == FailureKind.Validation)
    {
    }

    foreach (var duplicatePolicyCommand in new[] { "SET_CHOKE", "set_choke ", " SET_CHOKE" })
    {
        try
        {
            await gate.SetPolicyAsync(logic.Logic.Id, true, ControlSinkKinds.Simulator,
                [new AutonomyCommandLimit("SET_CHOKE", "spm", 1m, 2m), new AutonomyCommandLimit(duplicatePolicyCommand, "spm", 3m, 4m)],
                5, 3600, "admin", allowApprovedRecommendations: true);
            throw new InvalidOperationException("A policy permitted the same command twice.");
        }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == FailureKind.Validation &&
                                                           exception.Code == "CONTROL_POLICY_LIMITS_INVALID")
        {
        }
    }

    var wrongMode = await gate.ExecuteApprovedAsync(supersededId, "operator");
    Require(wrongMode.RefusalCode == GateCodes.NotRecommendationMode,
        "A recommendation whose revision is no longer in Recommendation mode was executed.");

    // 6d. What happened after the command left: readback decides, a doubt is never recorded as a failure, and a
    // person can settle what nobody can verify.
    var readbackLogic = await ActiveConfigurationAsync(ExecutionMode.Recommendation, null);
    await gate.SetPolicyAsync(readbackLogic.Logic.Id, true, ControlSinkKinds.Simulator, limits, 20, 3600, "admin",
        allowApprovedRecommendations: true);

    async Task<ControlActionRecord> ExecuteAsync(
        (ConfiguredLogicDefinition Logic, ConfiguredLogicRevision Revision, Asset Well) active, decimal speed,
        SimulatedReadbackBehavior readback = SimulatedReadbackBehavior.Echo,
        SimulatedDispatchFault fault = SimulatedDispatchFault.None)
    {
        var id = await ApprovedRecommendationAsync(active, speed);
        simulator.Readback = readback;
        simulator.Fault = fault;
        try
        {
            return (await gate.ExecuteApprovedAsync(id, "operator")).Action ??
                   throw new InvalidOperationException("The approved recommendation was refused.");
        }
        finally
        {
            simulator.Readback = SimulatedReadbackBehavior.Echo;
            simulator.Fault = SimulatedDispatchFault.None;
        }
    }

    async Task RequireFailureAsync(Func<Task> action, FailureKind kind, string why)
    {
        try
        {
            await action();
        }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == kind)
        {
            return;
        }

        throw new InvalidOperationException($"{why}: expected a {kind} failure.");
    }

    // A target that reports a different value than commanded is ambiguous, and the recommendation is not Executed.
    var mismatch = await ExecuteAsync(readbackLogic, 9m, SimulatedReadbackBehavior.Mismatch);
    Require(mismatch is { Status: ControlActionStatus.Ambiguous, GateCode: GateCodes.ReadbackMismatch, ReadbackValue: 10m } &&
            await StatusAsync(mismatch.RecommendationId) == RecommendationStatus.Approved,
        "A readback that differs from the command must be Ambiguous and must not mark the recommendation Executed.");

    // A person settles it: attested, noted, audited under their name, and only once.
    await RequireFailureAsync(() => gate.ResolveAsync(mismatch.Id, true, "  ", "operator"), FailureKind.Validation,
        "A resolution without a note");
    var resolved = await gate.ResolveAsync(mismatch.Id, true, "Confirmed at the wellhead.", "operator");
    Require(resolved is { Status: ControlActionStatus.Verified, GateCode: GateCodes.OperatorResolvedApplied,
                ResolutionNote: "Confirmed at the wellhead." } &&
            await StatusAsync(mismatch.RecommendationId) == RecommendationStatus.Executed &&
            await db.AuditEvents.AnyAsync(item => item.TargetId == mismatch.Id.ToString() &&
                item.Action == "control.action.verified" && item.ActorIdentity == "operator" &&
                item.DetailsJson.Contains("Confirmed at the wellhead.")),
        "An operator-resolved action must be Verified with their note, and audited under their name.");
    await RequireFailureAsync(() => gate.ResolveAsync(mismatch.Id, false, "Changed my mind.", "operator"),
        FailureKind.Conflict, "Resolving an already settled action");
    await RequireFailureAsync(() => gate.VerifyAsync(mismatch.Id, "operator"), FailureKind.Conflict,
        "Verifying an already settled action");
    await RequireFailureAsync(() => gate.VerifyAsync(Guid.NewGuid(), "operator"), FailureKind.NotFound,
        "Verifying an unknown action");

    // A target that cannot answer leaves the action Dispatched; asking again later settles it.
    var unknown = await ExecuteAsync(readbackLogic, 9m, SimulatedReadbackBehavior.Unknown);
    Require(unknown.Status == ControlActionStatus.Dispatched &&
            await StatusAsync(unknown.RecommendationId) == RecommendationStatus.Executed,
        "An unanswerable readback must leave the action Dispatched.");
    Require((await gate.VerifyAsync(unknown.Id, "system:test")).Status == ControlActionStatus.Verified,
        "Verifying once the target answers must settle a Dispatched action.");

    // The call failed after the device applied the command: never recorded as failed, readback finds it applied.
    var afterApplying = await ExecuteAsync(readbackLogic, 10m, fault: SimulatedDispatchFault.FailAfterApplying);
    Require(afterApplying is { Status: ControlActionStatus.Ambiguous, GateCode: GateCodes.SinkAmbiguous } &&
            await StatusAsync(afterApplying.RecommendationId) == RecommendationStatus.Approved,
        "A sink exception must be Ambiguous, not Failed.");
    var confirmed = await gate.VerifyAsync(afterApplying.Id, "system:test");
    Require(confirmed is { Status: ControlActionStatus.Verified, GateCode: GateCodes.ReadbackConfirmed, ReadbackValue: 10m } &&
            confirmed.VerifiedAtUtc is not null &&
            await StatusAsync(afterApplying.RecommendationId) == RecommendationStatus.Executed,
        "Readback must confirm a command that was applied before the call failed.");

    // The call failed before the device was reached: readback finds nothing, and only then is it Failed.
    var beforeApplying = await ExecuteAsync(readbackLogic, 11m, fault: SimulatedDispatchFault.FailBeforeApplying);
    Require(beforeApplying.Status == ControlActionStatus.Ambiguous, "A sink exception before applying must still start Ambiguous.");
    var notApplied = await gate.VerifyAsync(beforeApplying.Id, "system:test");
    Require(notApplied is { Status: ControlActionStatus.Failed, GateCode: GateCodes.ReadbackNotApplied } &&
            await StatusAsync(beforeApplying.RecommendationId) == RecommendationStatus.Invalidated,
        "A command the target never received must end Failed and invalidate the recommendation.");
    Require((await gate.ResolveAsync(
                (await ExecuteAsync(readbackLogic, 8m, fault: SimulatedDispatchFault.FailBeforeApplying)).Id,
                false, "Valve was isolated.", "operator")) is { Status: ControlActionStatus.Failed, GateCode: GateCodes.OperatorResolvedNotApplied },
        "An operator must be able to resolve an ambiguous action as not applied.");

    // An ambiguous command may have moved the device, so it keeps counting against the rate limit.
    var rateLogic = await ActiveConfigurationAsync(ExecutionMode.Recommendation, null);
    await gate.SetPolicyAsync(rateLogic.Logic.Id, true, ControlSinkKinds.Simulator, limits, 1, 3600, "admin",
        allowApprovedRecommendations: true);
    Require((await ExecuteAsync(rateLogic, 9m, fault: SimulatedDispatchFault.FailAfterApplying)).Status == ControlActionStatus.Ambiguous,
        "The rate-limit scenario needs an ambiguous action.");
    var overRate = await gate.ExecuteApprovedAsync(await ApprovedRecommendationAsync(rateLogic, 9m), "operator");
    Require(overRate.RefusalCode == GateCodes.RateLimited, "An ambiguous action must count against the rate limit.");
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
    private DateTimeOffset current = utcNow;

    public override DateTimeOffset GetUtcNow() => current.ToUniversalTime();

    public void Advance(TimeSpan by) => current += by;
}
