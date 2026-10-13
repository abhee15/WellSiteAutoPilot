using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Recommendations;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Domain.Recommendations;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Logic;
using WellSiteAutoPilot.Persistence.Recommendations;

var connectionString = Environment.GetEnvironmentVariable("WSA_DATABASE_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("WSA_DATABASE_CONNECTION_STRING is required.");
}

var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var dbContext = new WellSiteAutoPilotDbContext(options);
var clock = new FixedTimeProvider(
    new DateTimeOffset(2026, 10, 8, 6, 0, 0, TimeSpan.Zero));

var assetRepository = new AssetRepository(dbContext);
var configuredLogicRepository = new ConfiguredLogicRepository(dbContext);
var executionRepository = new ExecutionRepository(dbContext);
var moduleRepository = new LogicModuleCatalogRepository(dbContext);
var recommendationRepository = new RecommendationRepository(dbContext);

var assetService = new AssetService(assetRepository, clock);
var moduleCatalog = new LogicModuleCatalogService(moduleRepository, clock);
var configuredLogicService = new ConfiguredLogicService(
    configuredLogicRepository,
    assetRepository,
    moduleRepository,
    clock);
var decisionService = new RecommendationDecisionService(recommendationRepository, clock);
var materializer = new RecommendationMaterializer(
    executionRepository,
    recommendationRepository,
    clock);

var wellType = await assetService.CreateAssetTypeAsync(
    new CreateAssetTypeCommand(
        $"recommendation-well-{Guid.NewGuid():N}",
        "Recommendation Well",
        """{"type":"object"}"""));

var well = await assetService.CreateAssetAsync(
    new CreateAssetCommand(
        wellType.Id,
        "Recommendation Well 101",
        null,
        """{"field":"recommendation"}"""));

var manifest = $$"""
{
  "manifestVersion": 1,
  "moduleId": "weatherford.recommendation-test",
  "version": "1.0.0",
  "displayName": "Recommendation Test",
  "publisher": "Weatherford",
  "runtime": "DotNet",
  "executionProfile": "ScheduledOneShot",
  "entryPoint": "Weatherford.RecommendationTest.EntryPoint",
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
  "commandRequirements": [
    {
      "id": "set-speed",
      "assetRole": "well",
      "command": "SetSpeed",
      "quantity": "PumpingSpeed",
      "canonicalUnit": "spm"
    }
  ]
}
""";

await moduleCatalog.RegisterAsync(
    new RegisterLogicModuleCommand(
        manifest,
        new string('b', 64),
        LogicModuleTrustStatus.TrustedPublisher));

var configuredLogic = await configuredLogicService.CreateAsync(
    new CreateConfiguredLogicCommand(
        "Recommendation Pump Controller",
        manifest,
        """{"targetFillage":75}""",
        [
            new ConfiguredLogicAssetBindingCommand(
                "well",
                well.Id,
                "{}")
        ],
        [
            new ConfiguredLogicDataBindingCommand(
                "pump-fillage",
                well.Id,
                "simulator",
                "WELL-101",
                "{}")
        ],
        null));

var revision = configuredLogic.Revisions.Single();
await configuredLogicService.ValidateRevisionAsync(
    configuredLogic.Id,
    revision.Id);
await configuredLogicService.ActivateRevisionAsync(
    configuredLogic.Id,
    revision.Id);

var command = new ConfiguredShadowExecutionCommand(
    configuredLogic.Id,
    revision.Id,
    revision.ModuleId,
    revision.ModuleVersion,
    revision.ParametersJson,
    [
        new ConfiguredExecutionAssetCommand(
            "well",
            well.Id,
            "{}")
    ],
    [
        new ConfiguredExecutionInputCommand(
            "pump-fillage",
            well.Id,
            "simulator",
            "WELL-101",
            "PumpFillage",
            "Current",
            "%",
            60,
            false,
            "{}")
    ],
    ExecutionTriggerKind.Manual,
    null);

var recommendationExecution = new ExecutionRecord(
    Guid.NewGuid(),
    configuredLogic.Id,
    revision.ModuleId,
    revision.ModuleVersion,
    revision.Id,
    null,
    null,
    null,
    ExecutionMode.Recommendation,
    ExecutionStatus.Requested,
    "ci-recommendation-materialization",
    clock.GetUtcNow(),
    2,
    ExecutionTriggerKind.Manual);

await executionRepository.AddConfiguredRequestedAsync(
    recommendationExecution,
    command);

var result = new LogicModuleExecutionResult(
    "RECOMMENDATION_TEST_COMPLETED",
    [],
    [
        new RecommendationIntentDraft(
            "INFORMATIONAL_ONLY",
            well.Id,
            "Informational draft",
            "This draft type is intentionally not materialized as a governed control recommendation.")
    ],
    [
        new ControlIntentDraft(
            "SET_PUMPING_SPEED",
            well.Id,
            "SetSpeed",
            "PumpingSpeed",
            9.5m,
            "spm",
            "OPTIMIZED_SPEED")
    ],
    new Dictionary<string, string>());

var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var outputJson = JsonSerializer.Serialize(result, serializerOptions);

await executionRepository.ApplyCompletedAsync(
    Guid.NewGuid(),
    "recommendation-test-result",
    recommendationExecution.Id,
    clock.GetUtcNow(),
    clock.GetUtcNow().AddSeconds(1),
    result.OutcomeCode,
    outputJson);

var created = await materializer.MaterializeAsync(
    recommendationExecution.Id,
    outputJson);

if (created != 1)
{
    throw new InvalidOperationException(
        "Recommendation materializer did not create exactly one governed control recommendation.");
}

var recommendations = await recommendationRepository.ListByExecutionAsync(
    recommendationExecution.Id);
var recommendation = recommendations.Single();

if (recommendation.Status != RecommendationStatus.Created ||
    recommendation.AssetId != well.Id ||
    recommendation.Code != "SET_PUMPING_SPEED" ||
    recommendation.Command != "SetSpeed" ||
    recommendation.Quantity != "PumpingSpeed" ||
    recommendation.SuggestedValue != 9.5m ||
    recommendation.Unit != "spm" ||
    recommendation.ReasonCode != "OPTIMIZED_SPEED" ||
    recommendation.ExpiresAtUtc is not null ||
    recommendation.ConfiguredLogicId != configuredLogic.Id ||
    recommendation.ConfigurationRevisionId != revision.Id)
{
    throw new InvalidOperationException(
        "Governed Recommendation did not preserve the expected immutable source snapshot.");
}

var duplicateCreated = await materializer.MaterializeAsync(
    recommendationExecution.Id,
    outputJson);

if (duplicateCreated != 0 ||
    (await recommendationRepository.ListByExecutionAsync(
        recommendationExecution.Id)).Count != 1)
{
    throw new InvalidOperationException(
        "Recommendation materialization was not idempotent.");
}

// Operators must still be able to reject stale or inactive suggestions.
var rejectable = recommendation with
{
    Id = Guid.NewGuid(),
    IntentIndex = recommendation.IntentIndex + 1
};
await recommendationRepository.AddMissingAsync([rejectable]);
await dbContext.Assets
    .Where(item => item.Id == well.Id)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(item => item.IsActive, false));

if (!await recommendationRepository.TryDecideAsync(
        rejectable.Id,
        RecommendationStatus.Rejected,
        "reviewer",
        "Asset deactivated",
        clock.GetUtcNow()))
{
    throw new InvalidOperationException("Inactive asset prevented safe rejection.");
}

var rejectionAudit = await dbContext.AuditEvents.AsNoTracking()
    .Where(item => item.TargetType == "recommendation" &&
        item.TargetId == rejectable.Id.ToString() &&
        item.Action == "recommendation.rejected")
    .ToArrayAsync();
if (rejectionAudit.Length != 1 ||
    rejectionAudit[0].AssetId != well.Id ||
    rejectionAudit[0].ActorIdentity != "reviewer")
{
    throw new InvalidOperationException("Recommendation rejection audit was not persisted exactly once.");
}

await dbContext.Assets
    .Where(item => item.Id == well.Id)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(item => item.IsActive, true));

await dbContext.Recommendations
    .Where(item => item.Id == rejectable.Id)
    .ExecuteDeleteAsync();

// A recommendation must not be approved after its source revision is superseded.
await dbContext.ConfiguredLogicDefinitions
    .Where(item => item.Id == configuredLogic.Id)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(item => item.ActiveRevisionId, (Guid?)null));

if (await recommendationRepository.TryDecideAsync(
        recommendation.Id,
        RecommendationStatus.Approved,
        "reviewer",
        "Stale revision",
        clock.GetUtcNow()))
{
    throw new InvalidOperationException("Superseded revision allowed approval.");
}

if ((await recommendationRepository.GetAsync(recommendation.Id))?.Status != RecommendationStatus.Created ||
    await dbContext.AuditEvents.CountAsync(item => item.TargetType == "recommendation" &&
        item.TargetId == recommendation.Id.ToString()) != 0)
{
    throw new InvalidOperationException("Superseded revision approval attempt changed state or audit.");
}

await dbContext.ConfiguredLogicDefinitions
    .Where(item => item.Id == configuredLogic.Id)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(item => item.ActiveRevisionId, revision.Id));

// An inactive asset must not receive a new approval.
await dbContext.Assets
    .Where(item => item.Id == well.Id)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(item => item.IsActive, false));

if (await recommendationRepository.TryDecideAsync(
        recommendation.Id,
        RecommendationStatus.Approved,
        "reviewer",
        "Inactive asset",
        clock.GetUtcNow()))
{
    throw new InvalidOperationException("Inactive asset allowed approval.");
}

if ((await recommendationRepository.GetAsync(recommendation.Id))?.Status != RecommendationStatus.Created ||
    await dbContext.AuditEvents.CountAsync(item => item.TargetType == "recommendation" &&
        item.TargetId == recommendation.Id.ToString()) != 0)
{
    throw new InvalidOperationException("Inactive asset approval attempt changed state or audit.");
}

await dbContext.Assets
    .Where(item => item.Id == well.Id)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(item => item.IsActive, true));

// Decisions must be based on a completed Recommendation-mode execution.
await dbContext.Executions
    .Where(item => item.Id == recommendationExecution.Id)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(item => item.Status, nameof(ExecutionStatus.Failed)));

if (await recommendationRepository.TryDecideAsync(
        recommendation.Id,
        RecommendationStatus.Approved,
        "reviewer",
        "Failed execution",
        clock.GetUtcNow()))
{
    throw new InvalidOperationException("Failed execution allowed recommendation approval.");
}

await dbContext.Executions
    .Where(item => item.Id == recommendationExecution.Id)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(item => item.Status, nameof(ExecutionStatus.Completed)));

// A decision must be blocked when the caller's authorized asset set excludes the target.
try
{
    await decisionService.DecideAsync(
        recommendation.Id,
        RecommendationStatus.Approved,
        "out-of-scope-reviewer",
        "Should not be authorized",
        [Guid.NewGuid()]);
    throw new InvalidOperationException("Out-of-scope recommendation was approved.");
}
catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException exception)
    when (exception.Code == "RECOMMENDATION_ASSET_SCOPE_FORBIDDEN")
{
}

// Invalid scope must not modify the recommendation or emit an audit event.
var beforeUnauthorizedAuditCount = await dbContext.AuditEvents
    .CountAsync(item => item.TargetType == "recommendation" &&
        item.TargetId == recommendation.Id.ToString());
if (beforeUnauthorizedAuditCount != 0 ||
    (await recommendationRepository.GetAsync(recommendation.Id))?.Status != RecommendationStatus.Created)
{
    throw new InvalidOperationException("Unauthorized decision modified state or wrote audit.");
}

// Invalid decision metadata must be rejected before any persistence transition.
foreach (var invalidActor in new[] { " ", new string('a', 257), "reviewer\nother" })
{
    try
    {
        await decisionService.DecideAsync(
            recommendation.Id,
            RecommendationStatus.Approved,
            invalidActor,
            null,
            [well.Id]);
        throw new InvalidOperationException("Invalid decision actor was accepted.");
    }
    catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException exception)
        when (exception.Code is "RECOMMENDATION_DECISION_INVALID" or "RECOMMENDATION_DECISION_METADATA_INVALID")
    {
    }

    if ((await recommendationRepository.GetAsync(recommendation.Id))?.Status != RecommendationStatus.Created ||
        await dbContext.AuditEvents.CountAsync(item => item.TargetType == "recommendation" &&
            item.TargetId == recommendation.Id.ToString()) != 0)
    {
        throw new InvalidOperationException("Invalid decision actor changed state or emitted an audit event.");
    }
}

// Invalid decision reasons must not persist a decision or an audit event.
foreach (var invalidReason in new[] { new string('x', 1025), "reason\nwith newline" })
{
    try
    {
        await decisionService.DecideAsync(
            recommendation.Id,
            RecommendationStatus.Approved,
            "reviewer",
            invalidReason,
            [well.Id]);
        throw new InvalidOperationException("Invalid decision reason was accepted.");
    }
    catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException exception)
        when (exception.Code == "RECOMMENDATION_DECISION_METADATA_INVALID")
    {
    }

    if ((await recommendationRepository.GetAsync(recommendation.Id))?.Status != RecommendationStatus.Created ||
        await dbContext.AuditEvents.CountAsync(item => item.TargetType == "recommendation" &&
            item.TargetId == recommendation.Id.ToString()) != 0)
    {
        throw new InvalidOperationException("Invalid reason modified state or audit.");
    }
}

// An explicitly empty authorized asset set must not grant access to the recommendation.
try
{
    await decisionService.DecideAsync(
        recommendation.Id,
        RecommendationStatus.Approved,
        "unscoped-reviewer",
        "No authorized assets",
        []);
    throw new InvalidOperationException("Empty authorized asset scope allowed approval.");
}
catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException exception)
    when (exception.Code == "RECOMMENDATION_ASSET_SCOPE_FORBIDDEN")
{
}

if ((await recommendationRepository.GetAsync(recommendation.Id))?.Status != RecommendationStatus.Created ||
    await dbContext.AuditEvents.CountAsync(item => item.TargetType == "recommendation" &&
        item.TargetId == recommendation.Id.ToString()) != 0)
{
    throw new InvalidOperationException("Empty asset scope changed recommendation state or emitted an audit event.");
}

// Approval is a compare-and-set transition; repeated or competing decisions must fail.
var decidedAt = clock.GetUtcNow().AddMinutes(2);
var approvedDecision = await decisionService.DecideAsync(
    recommendation.Id,
    RecommendationStatus.Approved,
    "  FIELD\\reviewer  ",
    "Validated against operational conditions",
    [well.Id]);

if (await recommendationRepository.TryDecideAsync(
        recommendation.Id,
        RecommendationStatus.Rejected,
        "FIELD\\other-reviewer",
        "Conflicting decision",
        decidedAt.AddSeconds(1)))
{
    throw new InvalidOperationException("A second decision overwrote approval.");
}

var approvalAudit = await dbContext.AuditEvents.AsNoTracking()
    .Where(item => item.TargetType == "recommendation" &&
        item.TargetId == recommendation.Id.ToString() &&
        item.Action == "recommendation.approved")
    .ToArrayAsync();
if (approvalAudit.Length != 1 ||
    approvalAudit[0].AssetId != well.Id ||
    approvalAudit[0].ActorIdentity != "FIELD\\reviewer")
{
    throw new InvalidOperationException("Recommendation approval audit was not persisted exactly once.");
}

var decisionAuditCount = await dbContext.AuditEvents.AsNoTracking()
    .CountAsync(item => item.TargetType == "recommendation" &&
        item.TargetId == recommendation.Id.ToString());
if (decisionAuditCount != 1)
{
    throw new InvalidOperationException("Competing decision created duplicate audit events.");
}

var approved = (await recommendationRepository.ListByExecutionAsync(
    recommendationExecution.Id)).Single();
if (approved.Status != RecommendationStatus.Approved ||
    approved.DecisionBy != "FIELD\\reviewer" ||
    approved.DecisionAtUtc != clock.GetUtcNow() ||
    approved.ControlActionId is not null)
{
    throw new InvalidOperationException(
        "Recommendation approval was not persisted or incorrectly created a control action.");
}

try
{
    await decisionService.DecideAsync(
        recommendation.Id,
        RecommendationStatus.Rejected,
        "second-reviewer",
        "Attempt to overwrite an approval",
        [well.Id]);
    throw new InvalidOperationException("Application service accepted a second decision.");
}
catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException exception)
    when (exception.Code == "RECOMMENDATION_NOT_DECIDABLE")
{
}

// Expiration and invalid transitions are rejected without changing approved state.
if (await recommendationRepository.TryDecideAsync(
        recommendation.Id,
        RecommendationStatus.Rejected,
        "late-reviewer",
        "Late attempt",
        clock.GetUtcNow().AddDays(1)))
{
    throw new InvalidOperationException("A decided recommendation accepted a late rejection.");
}

try
{
    await recommendationRepository.TryDecideAsync(
        recommendation.Id,
        RecommendationStatus.Executed,
        "reviewer",
        null,
        clock.GetUtcNow());
    throw new InvalidOperationException("Unsupported decision state was accepted.");
}
catch (ArgumentOutOfRangeException)
{
}

// Competing decisions from separate database contexts must produce exactly one winner
// and exactly one durable audit event, regardless of which request wins.
var contested = recommendation with
{
    Id = Guid.NewGuid(),
    IntentIndex = recommendation.IntentIndex + 2,
    Status = RecommendationStatus.Created,
    DecisionBy = null,
    DecisionAtUtc = null,
    DecisionReason = null
};
await recommendationRepository.AddMissingAsync([contested]);

async Task<bool> CompeteAsync(RecommendationStatus status, string actor)
{
    await using var competingContext = new WellSiteAutoPilotDbContext(options);
    var competingRepository = new RecommendationRepository(competingContext);
    return await competingRepository.TryDecideAsync(
        contested.Id,
        status,
        actor,
        "Concurrent decision regression",
        clock.GetUtcNow());
}

var competingResults = await Task.WhenAll(
    CompeteAsync(RecommendationStatus.Approved, "concurrent-approver"),
    CompeteAsync(RecommendationStatus.Rejected, "concurrent-rejector"));

if (competingResults.Count(won => won) != 1)
{
    throw new InvalidOperationException(
        "Concurrent recommendation decisions did not produce exactly one winner.");
}

var contestedState = await recommendationRepository.GetAsync(contested.Id);
var contestedAudit = await dbContext.AuditEvents.AsNoTracking()
    .Where(item => item.TargetType == "recommendation" &&
        item.TargetId == contested.Id.ToString())
    .ToArrayAsync();

if (contestedState is null ||
    contestedAudit.Length != 1 ||
    contestedState.Status != (competingResults[0]
        ? RecommendationStatus.Approved
        : RecommendationStatus.Rejected) ||
    contestedState.DecisionBy != (competingResults[0]
        ? "concurrent-approver"
        : "concurrent-rejector") ||
    contestedAudit[0].ActorIdentity != contestedState.DecisionBy ||
    contestedAudit[0].Action != (competingResults[0]
        ? "recommendation.approved"
        : "recommendation.rejected") ||
    contestedState.ControlActionId is not null)
{
    throw new InvalidOperationException(
        "Concurrent decision state, audit, or control-action isolation diverged.");
}

var shadowExecution = recommendationExecution with
{
    Id = Guid.NewGuid(),
    Mode = ExecutionMode.Shadow,
    CorrelationId = "ci-shadow-control-intent",
    RequestedAtUtc = clock.GetUtcNow().AddMinutes(1)
};

await executionRepository.AddConfiguredRequestedAsync(
    shadowExecution,
    command);

await executionRepository.ApplyCompletedAsync(
    Guid.NewGuid(),
    "recommendation-test-result",
    shadowExecution.Id,
    shadowExecution.RequestedAtUtc,
    shadowExecution.RequestedAtUtc.AddSeconds(1),
    result.OutcomeCode,
    outputJson);

var shadowCreated = await materializer.MaterializeAsync(
    shadowExecution.Id,
    outputJson);

if (shadowCreated != 0 ||
    (await recommendationRepository.ListByExecutionAsync(
        shadowExecution.Id)).Count != 0)
{
    throw new InvalidOperationException(
        "Shadow execution incorrectly materialized a governed Recommendation.");
}

var persistedEntity = await dbContext.Recommendations
    .AsNoTracking()
    .SingleAsync(item => item.Id == recommendation.Id);

if (string.IsNullOrWhiteSpace(persistedEntity.IntentJson) ||
    persistedEntity.DecisionAtUtc != clock.GetUtcNow() ||
    persistedEntity.DecisionBy != "FIELD\\reviewer" ||
    persistedEntity.ControlActionId is not null)
{
    throw new InvalidOperationException(
        "Approved recommendation lost its source snapshot, decision metadata, or control-action isolation.");
}

Console.WriteLine(
    "Recommendation materialization, immutable source snapshot, Shadow isolation, and idempotency checks passed.");
return 0;

file sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow.ToUniversalTime();
}
