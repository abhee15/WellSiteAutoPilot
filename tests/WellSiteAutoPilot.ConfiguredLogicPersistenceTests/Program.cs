using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Logic;
using WellSiteAutoPilot.Failures;

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
var moduleCatalogRepository = new LogicModuleCatalogRepository(dbContext);
var moduleCatalogService = new LogicModuleCatalogService(
    moduleCatalogRepository,
    TimeProvider.System);
var assetService = new AssetService(assetRepository, TimeProvider.System);
var configuredLogicService = new ConfiguredLogicService(
    configuredLogicRepository,
    assetRepository,
    moduleCatalogRepository,
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

await moduleCatalogService.RegisterAsync(
    new RegisterLogicModuleCommand(
        manifest,
        "1111111111111111111111111111111111111111111111111111111111111111",
        LogicModuleTrustStatus.TrustedPublisher));

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
        ],
        [
            new ConfiguredLogicDataBindingCommand(
                "pump-fillage",
                well.Id,
                "provider-simulator",
                "well-cl-101",
                """{"source":"PumpFillage"}""")
        ],
        new ConfiguredLogicScheduleCommand(
            true,
            60,
            DateTimeOffset.UtcNow,
            "UTC")));

var revision1 = created.Revisions.Single();

if (created.ActiveRevisionId is not null ||
    revision1.RevisionNumber != 1 ||
    revision1.Mode != ExecutionMode.Shadow ||
    revision1.Status != ConfiguredLogicRevisionStatus.Draft ||
    revision1.AssetBindings.Single().AssetId != well.Id ||
    revision1.DataBindings.Single().AssetId != well.Id ||
    revision1.Schedule is null ||
    revision1.Schedule.CadenceSeconds != 60)
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

var manifestV11 = manifest.Replace("\"version\": \"1.0.0\"", "\"version\": \"1.1.0\"");

await moduleCatalogService.RegisterAsync(
    new RegisterLogicModuleCommand(
        manifestV11,
        "2222222222222222222222222222222222222222222222222222222222222222",
        LogicModuleTrustStatus.TrustedPublisher));

var revision2 = await configuredLogicService.CreateRevisionAsync(
    created.Id,
    new CreateConfiguredLogicRevisionCommand(
        manifestV11,
        """{"targetFillage":78}""",
        [
            new ConfiguredLogicAssetBindingCommand(
                "well",
                well.Id,
                """{"targetFillage":82}""")
        ],
        [
            new ConfiguredLogicDataBindingCommand(
                "pump-fillage",
                well.Id,
                "provider-simulator",
                "well-cl-101",
                """{"source":"PumpFillage"}""")
        ],
        new ConfiguredLogicScheduleCommand(
            true,
            120,
            DateTimeOffset.UtcNow,
            "UTC")));

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
    secondRoundTrip.DataBindings.Single().ProviderId != "provider-simulator" ||
    secondRoundTrip.Schedule?.CadenceSeconds != 120 ||
    roundTrip.Revisions.Count != 2)
{
    throw new InvalidOperationException(
        "Configured Logic revision activation did not preserve immutable revision history.");
}

var manifestV12 = manifest.Replace("\"version\": \"1.0.0\"", "\"version\": \"1.2.0\"");

await moduleCatalogService.RegisterAsync(
    new RegisterLogicModuleCommand(
        manifestV12,
        "3333333333333333333333333333333333333333333333333333333333333333",
        LogicModuleTrustStatus.Untrusted));

var untrustedRevision = await configuredLogicService.CreateRevisionAsync(
    created.Id,
    new CreateConfiguredLogicRevisionCommand(
        manifestV12,
        """{"targetFillage":79}""",
        [
            new ConfiguredLogicAssetBindingCommand(
                "well",
                well.Id,
                """{"targetFillage":83}""")
        ],
        [
            new ConfiguredLogicDataBindingCommand(
                "pump-fillage",
                well.Id,
                "provider-simulator",
                "well-cl-101",
                """{"source":"PumpFillage"}""")
        ],
        new ConfiguredLogicScheduleCommand(
            true,
            180,
            DateTimeOffset.UtcNow,
            "UTC")));

try
{
    await configuredLogicService.ValidateRevisionAsync(
        created.Id,
        untrustedRevision.Id);

    throw new InvalidOperationException(
        "Untrusted Logic Module revision was allowed to validate.");
}
catch (WellSiteAutoPilotException exception) when (
    exception.Code == "CONFIGURED_LOGIC_MODULE_NOT_TRUSTED")
{
}

// Reproduce races with separate EF contexts, never shared tracked entities.
var raceCommand = new CreateConfiguredLogicRevisionCommand(manifest, "{}",
    [new ConfiguredLogicAssetBindingCommand("well", well.Id, "{}")],
    [new ConfiguredLogicDataBindingCommand("pump-fillage", well.Id,
        "provider-simulator", "well-cl-101", "{}")], null);
var candidateA = await configuredLogicService.CreateRevisionAsync(created.Id, raceCommand);
var candidateB = await configuredLogicService.CreateRevisionAsync(created.Id, raceCommand);
await configuredLogicService.ValidateRevisionAsync(created.Id, candidateA.Id);
await configuredLogicService.ValidateRevisionAsync(created.Id, candidateB.Id);
var beforeRace = await configuredLogicService.GetRequiredAsync(created.Id);
var startRace = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
async Task<bool> CompeteActivation(Guid revisionId)
{
    await using var context = new WellSiteAutoPilotDbContext(options);
    await startRace.Task;
    try
    {
        await new ConfiguredLogicRepository(context).ActivateRevisionAsync(created.Id,
            revisionId, DateTimeOffset.UtcNow, beforeRace.ActiveRevisionId);
        return true;
    }
    catch (WellSiteAutoPilotException e) when (e.Code == "CONFIGURED_LOGIC_REVISION_CONFLICT")
    { return false; }
}
var competitors = new[] { CompeteActivation(candidateA.Id), CompeteActivation(candidateB.Id) };
startRace.SetResult();
var winners = await Task.WhenAll(competitors);
if (winners.Count(x => x) != 1) throw new InvalidOperationException("Concurrent activation must have exactly one winner.");
var afterRace = await configuredLogicService.GetRequiredAsync(created.Id);
if (afterRace.Revisions.Count(r => r.Status == ConfiguredLogicRevisionStatus.Active) != 1 ||
    afterRace.Revisions.Single(r => r.Status == ConfiguredLogicRevisionStatus.Active).Id != afterRace.ActiveRevisionId)
    throw new InvalidOperationException("Activation must atomically supersede and update the pointer.");
var activeRaceId = afterRace.ActiveRevisionId!.Value;
try
{
    // Stale service validation must not turn a now Active row back to Validated.
    await configuredLogicRepository.SetRevisionValidatedAsync(created.Id, activeRaceId, DateTimeOffset.UtcNow);
    throw new InvalidOperationException("Stale validation overwrote activation.");
}
catch (WellSiteAutoPilotException e) when (e.Code == "CONFIGURED_LOGIC_REVISION_CONFLICT") { }
var loser = candidateA.Id == activeRaceId ? candidateB : candidateA;
try
{
    await configuredLogicService.ActivateRevisionAsync(created.Id, loser.Id,
        expectedActivationToken: beforeRace.ActiveRevisionId?.ToString("D") ?? "none");
    throw new InvalidOperationException("Stale operator activation was accepted.");
}
catch (WellSiteAutoPilotException e) when (e.Code == "CONFIGURED_LOGIC_REVISION_CONFLICT") { }

// Both writers have the same stale next revision number; one commits and
// the other gets a domain 409 instead of an unhandled PostgreSQL 23505.
var nextNumber = afterRace.Revisions.Max(r => r.RevisionNumber) + 1;
var simultaneous = candidateA with { Id = Guid.NewGuid(), RevisionNumber = nextNumber,
    Status = ConfiguredLogicRevisionStatus.Draft, ValidatedAtUtc = null };
var startCreate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
async Task<bool> CompeteCreate()
{
    await using var context = new WellSiteAutoPilotDbContext(options);
    await startCreate.Task;
    try { await new ConfiguredLogicRepository(context).AddRevisionAsync(simultaneous with { Id = Guid.NewGuid() }); return true; }
    catch (WellSiteAutoPilotException e) when (e.Code == "CONFIGURED_LOGIC_REVISION_CONFLICT") { return false; }
}
var creators = new[] { CompeteCreate(), CompeteCreate() };
startCreate.SetResult();
if ((await Task.WhenAll(creators)).Count(x => x) != 1)
    throw new InvalidOperationException("Concurrent stale revision numbers must conflict safely.");
foreach (var invalidJson in new[] { "[]", "null", "1", "{\"x\":1,\"x\":2}", "{\"x\":{\"y\":1,\"y\":2}}" })
{
    try
    {
        await configuredLogicService.CreateRevisionAsync(created.Id, raceCommand with { ParametersJson = invalidJson });
        throw new InvalidOperationException("Invalid parameter object accepted.");
    }
    catch (WellSiteAutoPilotException e) when (e.Kind == FailureKind.Validation) { }
}
var inactiveCandidate = await configuredLogicService.CreateRevisionAsync(created.Id, raceCommand);
await configuredLogicService.ValidateRevisionAsync(created.Id, inactiveCandidate.Id);
await dbContext.Assets.Where(a => a.Id == well.Id)
    .ExecuteUpdateAsync(set => set.SetProperty(a => a.IsActive, false));
try
{
    await configuredLogicService.ActivateRevisionAsync(created.Id, inactiveCandidate.Id);
    throw new InvalidOperationException("Activation accepted an Asset retired after validation.");
}
catch (WellSiteAutoPilotException e) when (e.Code == "CONFIGURED_LOGIC_ASSET_NOT_FOUND") { }
await dbContext.Assets.Where(a => a.Id == well.Id)
    .ExecuteUpdateAsync(set => set.SetProperty(a => a.IsActive, true));
if ((await configuredLogicService.GetRequiredAsync(created.Id)).ActiveRevisionId != activeRaceId)
    throw new InvalidOperationException("Rejected activation changed the current revision.");
var activationAudits = await dbContext.AuditEvents.AsNoTracking()
    .Where(a => a.TargetId == created.Id.ToString() && a.Action == "CONFIGURED_LOGIC_REVISION_ACTIVATED")
    .CountAsync();
if (activationAudits != 3) throw new InvalidOperationException("Activation audit must commit once per successful transition only.");

Console.WriteLine(
    "Configured Logic persistence, module readiness, trust gating, activation, and revision-history checks passed.");
return 0;
