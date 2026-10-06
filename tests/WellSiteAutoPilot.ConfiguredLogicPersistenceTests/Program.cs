using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;

var connectionString = Environment.GetEnvironmentVariable("WSA_DATABASE_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("WSA_DATABASE_CONNECTION_STRING is required.");
}

var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var dbContext = new WellSiteAutoPilotDbContext(options);
var assetRepository = new AssetRepository(dbContext);
var configuredLogicRepository = new ConfiguredLogicRepository(dbContext);
var assetService = new AssetService(assetRepository, TimeProvider.System);
var configuredLogicService = new ConfiguredLogicService(
    configuredLogicRepository,
    assetRepository,
    TimeProvider.System);

var wellType = await assetService.CreateAssetTypeAsync(
    new CreateAssetTypeCommand(
        $"well-configured-{Guid.NewGuid():N}",
        "Configured Logic Well",
        """{"type":"object"}"""));

var well = await assetService.CreateAssetAsync(
    new CreateAssetCommand(
        wellType.Id,
        "Well CL-101",
        null,
        """{"field":"ConfiguredLogic"}"""));

var manifest = $$"""
{
  "manifestVersion": 1,
  "moduleId": "weatherford.configured-logic-test",
  "version": "1.0.0",
  "displayName": "Configured Logic Test",
  "publisher": "Weatherford",
  "runtime": "DotNet",
  "executionProfile": "ScheduledOneShot",
  "entryPoint": "Weatherford.ConfiguredLogicTest.EntryPoint",
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

var created = await configuredLogicService.CreateAsync(
    new CreateConfiguredLogicCommand(
        "Pump Fillage Observer",
        manifest,
        """{"targetFillage":75}""",
        [
            new ConfiguredLogicAssetBindingCommand(
                "well",
                well.Id,
                """{"targetFillage":80}""")
        ]));

var revision1 = created.Revisions.Single();

if (created.ActiveRevisionId is not null ||
    revision1.RevisionNumber != 1 ||
    revision1.Mode != ExecutionMode.Shadow ||
    revision1.Status != ConfiguredLogicRevisionStatus.Draft ||
    revision1.AssetBindings.Single().AssetId != well.Id)
{
    throw new InvalidOperationException(
        "New Configured Logic did not persist as a Shadow Draft with the expected Asset binding.");
}

var validated1 = await configuredLogicService.ValidateRevisionAsync(
    created.Id,
    revision1.Id);

if (validated1.Status != ConfiguredLogicRevisionStatus.Validated ||
    validated1.ValidatedAtUtc is null)
{
    throw new InvalidOperationException("Configured Logic revision did not validate.");
}

var active1 = await configuredLogicService.ActivateRevisionAsync(
    created.Id,
    revision1.Id);

if (active1.Status != ConfiguredLogicRevisionStatus.Active ||
    active1.ActivatedAtUtc is null)
{
    throw new InvalidOperationException("Configured Logic revision did not activate.");
}

var revision2 = await configuredLogicService.CreateRevisionAsync(
    created.Id,
    new CreateConfiguredLogicRevisionCommand(
        manifest.Replace("\"version\": \"1.0.0\"", "\"version\": \"1.1.0\""),
        """{"targetFillage":78}""",
        [
            new ConfiguredLogicAssetBindingCommand(
                "well",
                well.Id,
                """{"targetFillage":82}""")
        ]));

if (revision2.RevisionNumber != 2 ||
    revision2.Status != ConfiguredLogicRevisionStatus.Draft ||
    revision2.Mode != ExecutionMode.Shadow)
{
    throw new InvalidOperationException("Second Configured Logic revision was not created correctly.");
}

await configuredLogicService.ValidateRevisionAsync(created.Id, revision2.Id);
await configuredLogicService.ActivateRevisionAsync(created.Id, revision2.Id);

var roundTrip = await configuredLogicService.GetRequiredAsync(created.Id);
var firstRoundTrip = roundTrip.Revisions.Single(item => item.Id == revision1.Id);
var secondRoundTrip = roundTrip.Revisions.Single(item => item.Id == revision2.Id);

if (roundTrip.ActiveRevisionId != revision2.Id ||
    firstRoundTrip.Status != ConfiguredLogicRevisionStatus.Superseded ||
    secondRoundTrip.Status != ConfiguredLogicRevisionStatus.Active ||
    secondRoundTrip.ModuleVersion != "1.1.0" ||
    roundTrip.Revisions.Count != 2)
{
    throw new InvalidOperationException(
        "Configured Logic revision activation did not preserve immutable revision history.");
}

Console.WriteLine(
    "Configured Logic draft, Asset binding, validation, activation, and revision-history checks passed.");
return 0;
