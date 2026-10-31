using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Recommendations;
using WellSiteAutoPilot.Domain.Control;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Domain.Recommendations;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Control;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Logic;
using WellSiteAutoPilot.Persistence.Recommendations;

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
var controlRepository = new ControlRepository(db);

var assetService = new AssetService(assetRepository, clock);
var moduleCatalog = new LogicModuleCatalogService(moduleRepository, clock);
var configuredLogicService = new ConfiguredLogicService(configuredLogicRepository, assetRepository, moduleRepository, clock);
var decisionService = new RecommendationDecisionService(recommendationRepository, clock);
var materializer = new RecommendationMaterializer(executionRepository, recommendationRepository, clock);
var simulator = new SimulatedControlCommandSink();
var gateOptions = new AutonomousControlOptions { Enabled = true };
var dryRunReader = new FixedCurrentValueReader(7.5m);
var dryRun = new DryRunControlCommandSink(dryRunReader, clock);
var gate = new AutonomousControlService(controlRepository, recommendationRepository, executionRepository,
    configuredLogicRepository, [simulator, new RefusingProviderControlCommandSink(), dryRun], gateOptions, clock);

var suffix = Guid.NewGuid().ToString("N");
var wellType = await assetService.CreateAssetTypeAsync(new CreateAssetTypeCommand(
    $"mode-well-{suffix}", "Mode Well", """{"type":"object"}"""));
var well = await assetService.CreateAssetAsync(new CreateAssetCommand(wellType.Id, $"Mode Well {suffix}", null, "{}"));

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

var killSwitchTouched = false;
try
{
    await RunAsync();
}
finally
{
    if (killSwitchTouched)
        await controlRepository.SetKillSwitchAsync(false, "mode-safety-test", "test cleanup", clock.GetUtcNow());
}

Console.WriteLine("Execution mode safety tests passed.");
return;

async Task<(Guid ConfiguredLogicId, Guid ExecutionId, int Created)> RunExecutionAsync(
    ExecutionMode mode, bool advisory, params decimal[] speeds)
{
    var logic = await configuredLogicService.CreateAsync(new CreateConfiguredLogicCommand(
        $"Mode {mode} {Guid.NewGuid():N}", manifest, "{}",
        [new ConfiguredLogicAssetBindingCommand("well", well.Id, "{}")],
        [new ConfiguredLogicDataBindingCommand("pump-fillage", well.Id, "simulator", "WELL-1", "{}")],
        null, Mode: mode));
    var revision = logic.Revisions.Single();
    if (revision.Mode != mode) throw new InvalidOperationException($"Revision did not persist mode {mode}.");
    await configuredLogicService.ValidateRevisionAsync(logic.Id, revision.Id);
    await configuredLogicService.ActivateRevisionAsync(logic.Id, revision.Id);

    var command = new ConfiguredShadowExecutionCommand(logic.Id, revision.Id, revision.ModuleId, revision.ModuleVersion,
        revision.ParametersJson,
        [new ConfiguredExecutionAssetCommand("well", well.Id, "{}")],
        [new ConfiguredExecutionInputCommand("pump-fillage", well.Id, "simulator", "WELL-1", "PumpFillage", "Current", "%", 60, false, "{}")],
        ExecutionTriggerKind.Manual, null, mode);
    var execution = new ExecutionRecord(Guid.NewGuid(), logic.Id, revision.ModuleId, revision.ModuleVersion, revision.Id,
        null, null, null, mode, ExecutionStatus.Requested, $"mode-safety-{Guid.NewGuid():N}", clock.GetUtcNow(), 2,
        ExecutionTriggerKind.Manual);
    await executionRepository.AddConfiguredRequestedAsync(execution, command, null);

    var result = new LogicModuleExecutionResult("MODE_TEST_COMPLETED", [],
        advisory ? [new RecommendationIntentDraft("REVIEW_FILLAGE", well.Id, "Review fillage", "Fillage is low.", "PumpFillage", 40m, "%")] : [],
        speeds.Select(speed => new ControlIntentDraft("SET_PUMPING_SPEED", well.Id, "SetSpeed", "PumpingSpeed", speed, "spm", "OPTIMIZED")).ToArray(),
        new Dictionary<string, string>());
    var output = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
    await executionRepository.ApplyCompletedAsync(Guid.NewGuid(), $"mode-safety-result-{Guid.NewGuid():N}", execution.Id,
        clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(1), result.OutcomeCode, output);
    var created = await materializer.MaterializeAsync(execution.Id, output);
    return (logic.Id, execution.Id, created);
}

async Task<int> ActionCountAsync(Guid configuredLogicId) =>
    await db.ControlActions.CountAsync(item => item.ConfiguredLogicId == configuredLogicId);

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

async Task RunAsync()
{
    // Shadow: intents are observed only; no recommendation, no ControlAction, no sink call.
    var shadow = await RunExecutionAsync(ExecutionMode.Shadow, true, 9m);
    Require(shadow.Created == 0 && (await recommendationRepository.ListByExecutionAsync(shadow.ExecutionId)).Count == 0,
        "Shadow execution produced recommendations.");
    Require(await gate.ProcessExecutionAsync(shadow.ExecutionId) == 0 && await ActionCountAsync(shadow.ConfiguredLogicId) == 0,
        "Shadow execution reached the control gate.");

    // Recommendation: materializes; operator decisions never produce a command.
    var rec = await RunExecutionAsync(ExecutionMode.Recommendation, true, 9m, 10m);
    Require(rec.Created == 3, "Recommendation mode did not materialize its control and advisory intents.");
    var recs = await recommendationRepository.ListByExecutionAsync(rec.ExecutionId);
    Require(recs.Count(item => item.Command == RecommendationMaterializer.AdvisoryCommand && item.SuggestedValue == 40m) == 1,
        "The advisory recommendation was not materialized as advisory.");
    Require(recs.All(item => item.Status == RecommendationStatus.Created), "Recommendations must start Created.");
    Require(await gate.ProcessExecutionAsync(rec.ExecutionId) == 0, "Recommendation mode reached the control gate.");
    Require(await recommendationRepository.TryDecideAsync(recs.First().Id, RecommendationStatus.Approved, "operator", "ok", clock.GetUtcNow()),
        "Operator could not approve a Recommendation-mode recommendation.");
    Require(await recommendationRepository.TryDecideAsync(recs.Last().Id, RecommendationStatus.Rejected, "operator", "no", clock.GetUtcNow()),
        "Operator could not reject a Recommendation-mode recommendation.");
    Require(await ActionCountAsync(rec.ConfiguredLogicId) == 0 && simulator.Sent.Count == 0,
        "Recommendation mode issued a command.");
    Require(await db.AuditEvents.CountAsync(item => item.TargetType == "recommendation" &&
        (item.TargetId == recs.First().Id.ToString() || item.TargetId == recs.Last().Id.ToString())) == 2,
        "Operator decisions were not audited.");

    // Autonomous: operators cannot approve; nothing dispatches without a policy.
    var auto = await RunExecutionAsync(ExecutionMode.Autonomous, true, 9m, 50m, 10m, 11m);
    Require(auto.Created == 4, "Autonomous mode must materialize control intents only, not advisory drafts.");
    var autoRecs = (await recommendationRepository.ListByExecutionAsync(auto.ExecutionId)).ToArray();
    Require(!await recommendationRepository.TryDecideAsync(autoRecs[0].Id, RecommendationStatus.Approved, "operator", "x", clock.GetUtcNow()),
        "A human approved an Autonomous recommendation.");

    Require(await gate.ProcessExecutionAsync(auto.ExecutionId) == 4 && simulator.Sent.Count == 0,
        "Autonomous commands dispatched without a policy.");
    var noPolicy = await gate.ListActionsAsync(auto.ConfiguredLogicId, 50);
    Require(noPolicy.Count == 4 && noPolicy.All(item => item.Status == ControlActionStatus.Blocked && item.GateCode == GateCodes.NoPolicy),
        "Missing policy must block every intent with NO_POLICY.");

    // Real provider writes cannot be configured.
    try
    {
        await gate.SetPolicyAsync(auto.ConfiguredLogicId, true, ControlSinkKinds.Provider, [], 1, 60, "admin");
        throw new InvalidOperationException("A Provider sink policy was accepted.");
    }
    catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException)
    {
    }

    var limits = new[] { new AutonomyCommandLimit("SetSpeed", "spm", 5m, 12m) };

    // New execution with the policy: bounds, rate limit, and idempotency.
    await gate.SetPolicyAsync(auto.ConfiguredLogicId, true, ControlSinkKinds.Simulator, limits, 2, 3600, "admin");
    var gated = await RunExecutionAsync(ExecutionMode.Autonomous, true, 9m, 50m, 10m, 11m);
    await gate.SetPolicyAsync(gated.ConfiguredLogicId, true, ControlSinkKinds.Simulator, limits, 2, 3600, "admin");
    await gate.ProcessExecutionAsync(gated.ExecutionId);
    await gate.ProcessExecutionAsync(gated.ExecutionId);
    var actions = await gate.ListActionsAsync(gated.ConfiguredLogicId, 50);
    Require(actions.Count == 4, "Gate must record exactly one action per intent (idempotent).");
    Require(actions.Count(item => item.Status == ControlActionStatus.Dispatched) == 2 && simulator.Sent.Count == 2,
        "Exactly two in-bounds intents within the rate limit should dispatch.");
    Require(actions.Single(item => item.Value == 50m).GateCode == GateCodes.OutOfBounds, "Out-of-bounds intent was not blocked.");
    Require(actions.Single(item => item.Value == 11m).GateCode == GateCodes.RateLimited, "Rate limit did not block the third command.");
    Require(simulator.Sent.All(item => item.Value is 9m or 10m), "Unexpected command reached the sink.");
    var gatedRecs = await recommendationRepository.ListByExecutionAsync(gated.ExecutionId);
    Require(gatedRecs.Count(item => item.Status == RecommendationStatus.Executed && item.ControlActionId is not null) == 2,
        "Dispatched recommendations must be Executed and linked to their ControlAction.");
    Require(await db.AuditEvents.CountAsync(item => item.TargetType == "control-action" && item.Action == "control.action.dispatched") >= 2,
        "Dispatch was not audited.");

    // DryRun sink: gated like any other sink, records the would-be write with readback, never reaches the simulator.
    var dryRunCase = await RunExecutionAsync(ExecutionMode.Autonomous, false, 9m, 50m);
    await gate.SetPolicyAsync(dryRunCase.ConfiguredLogicId, true, ControlSinkKinds.DryRun, limits, 10, 3600, "admin");
    var simulatorBeforeDryRun = simulator.Sent.Count;
    await gate.ProcessExecutionAsync(dryRunCase.ExecutionId);
    var dryRunActions = await gate.ListActionsAsync(dryRunCase.ConfiguredLogicId, 10);
    Require(dryRun.Records.Count == 1 && dryRun.Records.Single().Request.Value == 9m &&
        dryRun.Records.Single().CurrentValue == 7.5m, "DryRun must record exactly the in-bounds intent with its readback.");
    Require(dryRunActions.Count(item => item.Status == ControlActionStatus.Dispatched && item.Sink == ControlSinkKinds.DryRun) == 1 &&
        dryRunActions.Single(item => item.Value == 50m).GateCode == GateCodes.OutOfBounds,
        "DryRun must keep the gate bounds.");
    Require(simulator.Sent.Count == simulatorBeforeDryRun, "DryRun intents must not reach another sink.");

    // Kill switch halts everything, even with a valid policy.
    killSwitchTouched = true;
    await gate.SetKillSwitchAsync(true, "operator", "test");
    var halted = await RunExecutionAsync(ExecutionMode.Autonomous, false, 9m);
    await gate.SetPolicyAsync(halted.ConfiguredLogicId, true, ControlSinkKinds.Simulator, limits, 10, 3600, "admin");
    var sentBefore = simulator.Sent.Count;
    await gate.ProcessExecutionAsync(halted.ExecutionId);
    var haltedActions = await gate.ListActionsAsync(halted.ConfiguredLogicId, 10);
    Require(simulator.Sent.Count == sentBefore && haltedActions.Single().GateCode == GateCodes.KillSwitch,
        "Kill switch did not halt autonomous dispatch.");
    await gate.SetKillSwitchAsync(false, "operator", "test over");

    // Global option off halts everything as well.
    gateOptions.Enabled = false;
    var globallyOff = await RunExecutionAsync(ExecutionMode.Autonomous, false, 9m);
    await gate.SetPolicyAsync(globallyOff.ConfiguredLogicId, true, ControlSinkKinds.Simulator, limits, 10, 3600, "admin");
    await gate.ProcessExecutionAsync(globallyOff.ExecutionId);
    Require(simulator.Sent.Count == sentBefore &&
        (await gate.ListActionsAsync(globallyOff.ConfiguredLogicId, 10)).Single().GateCode == GateCodes.GlobalDisabled,
        "Disabled global autonomous option did not block dispatch.");
    gateOptions.Enabled = true;
}

file sealed class FixedCurrentValueReader(decimal value) : IControlCurrentValueReader
{
    public Task<decimal?> ReadCurrentAsync(Guid assetId, string command, CancellationToken cancellationToken = default) =>
        Task.FromResult<decimal?>(value);
}

file sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow.ToUniversalTime();
}
