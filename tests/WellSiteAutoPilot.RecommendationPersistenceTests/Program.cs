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
    persistedEntity.DecisionAtUtc is not null ||
    persistedEntity.DecisionBy is not null ||
    persistedEntity.ControlActionId is not null)
{
    throw new InvalidOperationException(
        "New Recommendation incorrectly contained approval/control decision state.");
}

Console.WriteLine(
    "Recommendation materialization, immutable source snapshot, Shadow isolation, and idempotency checks passed.");
return 0;

file sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow.ToUniversalTime();
}
