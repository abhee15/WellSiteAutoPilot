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
var gate = new AutonomousControlService(controlRepository, recommendationRepository, executionRepository,
    configuredLogicRepository, [simulator, new RefusingProviderControlCommandSink()], gateOptions, clock);

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

await RunHttpSinkScenarioAsync();

Console.WriteLine("Execution mode safety tests passed.");
return;

// The HTTP simulator sink: idempotent dispatch, readback by key, and transport failure surfaces as an exception
// (which the service records as Ambiguous).
static async Task RunHttpSinkScenarioAsync()
{
    var handler = new StubSimulatorHandler();
    using var client = new HttpClient(handler) { BaseAddress = new Uri("http://simulator.test/") };
    var sink = new HttpSimulatorControlCommandSink(client);
    Require(sink.Kind == ControlSinkKinds.Simulator, "The HTTP sink must be a simulator sink.");
    var request = new ControlDispatchRequest(Guid.NewGuid(), Guid.NewGuid(), "SetSpeed", "CurrentSPM", 6m, "spm", "key-1");
    Require((await sink.ReadbackAsync(request)).State == ControlReadbackState.NotApplied, "Nothing is applied before dispatch.");
    var first = await sink.DispatchAsync(request);
    var second = await sink.DispatchAsync(request);
    Require(first.Accepted && first.Reference == second.Reference && handler.Applied.Count == 1, "Dispatch must be idempotent per key.");
    var readback = await sink.ReadbackAsync(request);
    Require(readback is { State: ControlReadbackState.Applied, ObservedValue: 6m }, "Readback must return the applied value.");
    handler.Down = true;
    var threw = false;
    try { await sink.DispatchAsync(request with { IdempotencyKey = "key-2" }); }
    catch (HttpRequestException) { threw = true; }
    Require(threw, "A simulator outage must surface as an exception so the action is Ambiguous.");
}

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
    Require(actions.Count(item => item.Status == ControlActionStatus.Verified && item.ReadbackValue == item.Value) == 2 && simulator.Sent.Count == 2,
        "Exactly two in-bounds intents within the rate limit should dispatch and verify by readback.");
    Require(actions.Single(item => item.Value == 50m).GateCode == GateCodes.OutOfBounds, "Out-of-bounds intent was not blocked.");
    Require(actions.Single(item => item.Value == 11m).GateCode == GateCodes.RateLimited, "Rate limit did not block the third command.");
    Require(simulator.Sent.All(item => item.Value is 9m or 10m), "Unexpected command reached the sink.");
    var gatedRecs = await recommendationRepository.ListByExecutionAsync(gated.ExecutionId);
    Require(gatedRecs.Count(item => item.Status == RecommendationStatus.Executed && item.ControlActionId is not null) == 2,
        "Dispatched recommendations must be Executed and linked to their ControlAction.");
    Require(await db.AuditEvents.CountAsync(item => item.TargetType == "control-action" && item.Action == "control.action.dispatched") >= 2,
        "Dispatch was not audited.");

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

    await RunApprovedRecommendationScenarioAsync(limits);
    await RunReadbackScenarioAsync(limits);
}

async Task RunApprovedRecommendationScenarioAsync(AutonomyCommandLimit[] limits)
{
    var run = await RunExecutionAsync(ExecutionMode.Recommendation, false, 9m, 50m, 10m);
    var items = (await recommendationRepository.ListByExecutionAsync(run.ExecutionId)).OrderBy(item => item.IntentIndex).ToArray();
    var sentBefore = simulator.Sent.Count;

    // Undecided recommendations can never be executed.
    await ExpectFailureAsync(() => gate.ExecuteApprovedAsync(items[0].Id, "control-admin"), "CONTROL_RECOMMENDATION_NOT_APPROVED");
    foreach (var item in items)
        Require(await recommendationRepository.TryDecideAsync(item.Id, RecommendationStatus.Approved, "operator", "ok", clock.GetUtcNow()),
            "Operator could not approve.");

    // No policy / no opt-in: refused, audited, nothing sent, recommendation stays Approved for a retry.
    var noPolicy = await gate.ExecuteApprovedAsync(items[0].Id, "control-admin");
    Require(noPolicy.Action is null && noPolicy.RefusalCode == GateCodes.NoPolicy, "Approved execution ran without a policy.");
    await gate.SetPolicyAsync(run.ConfiguredLogicId, true, ControlSinkKinds.Simulator, limits, 10, 3600, "admin");
    var noOptIn = await gate.ExecuteApprovedAsync(items[0].Id, "control-admin");
    Require(noOptIn.RefusalCode == GateCodes.ApprovedNotEnabled && simulator.Sent.Count == sentBefore,
        "Approved execution ran without the per-configuration opt-in.");
    Require(await db.AuditEvents.CountAsync(item => item.Action == "control.action.refused" && item.TargetId == items[0].Id.ToString()) == 2,
        "Refusals were not audited.");

    await gate.SetPolicyAsync(run.ConfiguredLogicId, true, ControlSinkKinds.Simulator, limits, 10, 3600, "admin", allowApprovedRecommendations: true);

    // Kill switch beats operator approval.
    killSwitchTouched = true;
    await gate.SetKillSwitchAsync(true, "operator", "test");
    Require((await gate.ExecuteApprovedAsync(items[0].Id, "control-admin")).RefusalCode == GateCodes.KillSwitch &&
        simulator.Sent.Count == sentBefore, "Kill switch did not stop an approved execution.");
    await gate.SetKillSwitchAsync(false, "operator", "test over");

    // Out of bounds is refused even though an operator approved it.
    Require((await gate.ExecuteApprovedAsync(items[1].Id, "control-admin")).RefusalCode == GateCodes.OutOfBounds,
        "Out-of-bounds approved recommendation was not refused.");

    // Expired approvals are refused.
    await db.Recommendations.Where(item => item.Id == items[2].Id)
        .ExecuteUpdateAsync(set => set.SetProperty(item => item.ExpiresAtUtc, clock.GetUtcNow().AddMinutes(-1)));
    Require((await gate.ExecuteApprovedAsync(items[2].Id, "control-admin")).RefusalCode == GateCodes.Expired,
        "Expired approved recommendation was not refused.");

    // Happy path: gate -> simulator sink, once; repeats return the same action (idempotent).
    var first = await gate.ExecuteApprovedAsync(items[0].Id, "control-admin");
    var again = await gate.ExecuteApprovedAsync(items[0].Id, "control-admin");
    Require(first.Action is { Status: ControlActionStatus.Verified, Origin: ControlOrigins.ApprovedRecommendation } &&
        again.Action?.Id == first.Action.Id && simulator.Sent.Count == sentBefore + 1,
        "Approved execution was not governed, verified and idempotent.");
    Require((await recommendationRepository.GetAsync(items[0].Id))!.Status == RecommendationStatus.Executed,
        "Executed approved recommendation was not marked Executed.");

    // The real provider sink is never reachable.
    var real = new RefusingProviderControlCommandSink();
    Require(!(await real.DispatchAsync(new ControlDispatchRequest(Guid.NewGuid(), well.Id, "SetSpeed", null, 9m, "spm", "k"))).Accepted,
        "The real provider sink accepted a command.");
}

async Task RunReadbackScenarioAsync(AutonomyCommandLimit[] limits)
{
    async Task<ControlActionRecord> AutonomousActionAsync()
    {
        var run = await RunExecutionAsync(ExecutionMode.Autonomous, false, 9m);
        await gate.SetPolicyAsync(run.ConfiguredLogicId, true, ControlSinkKinds.Simulator, limits, 10, 3600, "admin");
        await gate.ProcessExecutionAsync(run.ExecutionId);
        return (await gate.ListActionsAsync(run.ConfiguredLogicId, 5)).Single();
    }

    // Readback mismatch: ambiguous (recommendation no longer Executed) until re-verified.
    simulator.Readback = SimulatedReadbackBehavior.Mismatch;
    var mismatch = await AutonomousActionAsync();
    Require(mismatch.Status == ControlActionStatus.Ambiguous && mismatch.ReadbackValue == 10m &&
        (await recommendationRepository.GetAsync(mismatch.RecommendationId))!.Status == RecommendationStatus.Approved,
        "Readback mismatch did not leave the action Ambiguous.");
    simulator.Readback = SimulatedReadbackBehavior.Echo;
    var reverified = await gate.VerifyAsync(mismatch.Id, "control-admin");
    Require(reverified.Status == ControlActionStatus.Verified &&
        (await recommendationRepository.GetAsync(mismatch.RecommendationId))!.Status == RecommendationStatus.Executed,
        "Re-verification did not settle the ambiguous action.");

    // Sink timeout with unknown readback: ambiguous, then operator resolution with a mandatory note.
    simulator.FailAfterApplying = true;
    simulator.Readback = SimulatedReadbackBehavior.Unknown;
    var ambiguous = await AutonomousActionAsync();
    simulator.FailAfterApplying = false;
    Require(ambiguous.Status == ControlActionStatus.Ambiguous && ambiguous.GateCode == GateCodes.SinkAmbiguous,
        "A sink timeout was not recorded as Ambiguous.");
    await ExpectFailureAsync(() => gate.ResolveAsync(ambiguous.Id, false, " ", "control-admin"), "CONTROL_RESOLUTION_INVALID");
    var resolved = await gate.ResolveAsync(ambiguous.Id, false, "Confirmed on site: not applied.", "control-admin");
    Require(resolved.Status == ControlActionStatus.Failed && resolved.ResolutionNote is not null &&
        (await recommendationRepository.GetAsync(ambiguous.RecommendationId))!.Status == RecommendationStatus.Invalidated,
        "Operator resolution did not settle the ambiguous action.");
    await ExpectFailureAsync(() => gate.ResolveAsync(ambiguous.Id, true, "again", "control-admin"), "CONTROL_ACTION_NOT_RESOLVABLE");
    Require(await db.AuditEvents.CountAsync(item => item.TargetId == ambiguous.Id.ToString() && item.ActorIdentity == "control-admin") == 1,
        "Operator resolution was not audited.");
    simulator.Readback = SimulatedReadbackBehavior.Echo;
}

static async Task ExpectFailureAsync(Func<Task> action, string code)
{
    try
    {
        await action();
    }
    catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException exception) when (exception.Code == code)
    {
        return;
    }

    throw new InvalidOperationException($"Expected failure {code}.");
}

file sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow.ToUniversalTime();
}

file sealed class StubSimulatorHandler : HttpMessageHandler
{
    public Dictionary<string, decimal> Applied { get; } = [];

    public bool Down { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Down) return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
        if (request.Method == HttpMethod.Post)
        {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var key = doc.RootElement.GetProperty("idempotencyKey").GetString()!;
            Applied.TryAdd(key, doc.RootElement.GetProperty("value").GetDecimal());
            return Json($"{{\"reference\":\"ref-{key}\",\"state\":\"Applied\",\"observedValue\":{Applied[key]}}}");
        }

        var readKey = request.RequestUri!.Segments[^1];
        return Applied.TryGetValue(readKey, out var value)
            ? Json($"{{\"reference\":\"ref-{readKey}\",\"state\":\"Applied\",\"observedValue\":{value}}}")
            : Json("{\"reference\":\"\",\"state\":\"NotApplied\"}");
    }

    private static HttpResponseMessage Json(string body) =>
        new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}
