using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
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
var repository = new LogicModuleCatalogRepository(dbContext);
var catalog = new LogicModuleCatalogService(repository, TimeProvider.System);

const string manifest = """
{
  "manifestVersion": 1,
  "moduleId": "weatherford.catalog-test",
  "version": "1.0.0",
  "displayName": "Catalog Test",
  "publisher": "Weatherford",
  "runtime": "DotNet",
  "executionProfile": "ScheduledOneShot",
  "entryPoint": "Weatherford.CatalogTest.EntryPoint",
  "minimumSdkVersion": "1.0.0",
  "assetRequirements": [
    {
      "role": "well",
      "minimumCount": 1,
      "maximumCount": 1,
      "requiredAssetTypeKey": "well",
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

const string checksum =
    "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

var installed = await catalog.RegisterAsync(
    new RegisterLogicModuleCommand(
        manifest,
        checksum,
        LogicModuleTrustStatus.TrustedPublisher));

if (!installed.IsEnabled ||
    installed.TrustStatus != LogicModuleTrustStatus.TrustedPublisher)
{
    throw new InvalidOperationException("Trusted Logic Module was not enabled.");
}

var idempotent = await catalog.RegisterAsync(
    new RegisterLogicModuleCommand(
        manifest,
        checksum.ToUpperInvariant(),
        LogicModuleTrustStatus.TrustedPublisher));

if (idempotent.Id != installed.Id)
{
    throw new InvalidOperationException("Identical Logic Module registration was not idempotent.");
}

var roundTrip = await catalog.GetRequiredAsync(installed.ModuleId, installed.Version);
if (roundTrip.PackageSha256 != checksum ||
    roundTrip.ModuleId != "weatherford.catalog-test" ||
    roundTrip.Version != "1.0.0")
{
    throw new InvalidOperationException("Logic Module catalog persistence did not round-trip.");
}

var listed = await catalog.ListAsync("weatherford.catalog-test", 10);
if (listed.Count != 1 || listed.Single().Id != installed.Id)
{
    throw new InvalidOperationException("Logic Module catalog listing failed.");
}

try
{
    await catalog.RegisterAsync(
        new RegisterLogicModuleCommand(
            manifest,
            new string('a', 64),
            LogicModuleTrustStatus.TrustedPublisher));

    throw new InvalidOperationException("Immutable module version accepted different package content.");
}
catch (WellSiteAutoPilotException exception) when (
    exception.Code == "MODULE_VERSION_IMMUTABLE")
{
}

// Approval rules are pure: check every state against both actions.
InstalledLogicModule WithState(LogicModuleTrustStatus status, bool enabled) => installed with { TrustStatus = status, IsEnabled = enabled };
var notApproved = WithState(LogicModuleTrustStatus.Untrusted, false);
var approvedModule = WithState(LogicModuleTrustStatus.CustomerApproved, true);
var retiredModule = WithState(LogicModuleTrustStatus.CustomerApproved, false);
var approveNew = LogicModuleApproval.Decide(notApproved, LogicModuleApprovalAction.Approve);
if (approveNew is not { TrustStatus: LogicModuleTrustStatus.CustomerApproved, IsEnabled: true } ||
    LogicModuleApproval.Decide(approvedModule, LogicModuleApprovalAction.Approve) is not null ||
    LogicModuleApproval.Decide(WithState(LogicModuleTrustStatus.TrustedPublisher, true), LogicModuleApprovalAction.Approve) is not null)
{
    throw new InvalidOperationException("Approving a module did not follow the approval rules.");
}

var retire = LogicModuleApproval.Decide(WithState(LogicModuleTrustStatus.TrustedPublisher, true), LogicModuleApprovalAction.Retire);
if (retire is not { TrustStatus: LogicModuleTrustStatus.TrustedPublisher, IsEnabled: false } ||
    LogicModuleApproval.Decide(retiredModule, LogicModuleApprovalAction.Retire) is not null)
{
    throw new InvalidOperationException("Retiring a module did not follow the approval rules.");
}

foreach (var (module, action, code) in new[]
{
    (retiredModule, LogicModuleApprovalAction.Approve, "MODULE_RETIRED"),
    (notApproved, LogicModuleApprovalAction.Retire, "MODULE_NOT_APPROVED")
})
{
    try
    {
        LogicModuleApproval.Decide(module, action);
        throw new InvalidOperationException($"{code} was not raised.");
    }
    catch (WellSiteAutoPilotException exception) when (exception.Code == code && exception.Kind == FailureKind.Conflict)
    {
    }
}

foreach (var bad in new[] { null, "", "   ", new string('x', LogicModuleApproval.MaximumReasonLength + 1), "line\nbreak" })
{
    try
    {
        LogicModuleApproval.ValidateReason(bad);
        throw new InvalidOperationException("An invalid approval reason was accepted.");
    }
    catch (WellSiteAutoPilotException exception) when (exception.Kind == FailureKind.Validation)
    {
    }
}

if (LogicModuleApproval.ValidateReason("  Reviewed by operations  ") != "Reviewed by operations")
{
    throw new InvalidOperationException("The approval reason was not trimmed.");
}

// Structured schemas/capabilities are retained by the same catalog JSON snapshot.
var structuredJson = System.Text.Json.Nodes.JsonNode.Parse(manifest)!;
structuredJson["manifestVersion"] = 2;
structuredJson["moduleId"] = "weatherford.structured-catalog-test";
structuredJson["minimumSdkVersion"] = "1.2.0";
structuredJson["dataRequirements"]![0]!["requiredCapability"] = "structured.current";
structuredJson["dataRequirements"]![0]!["valueSchema"] = System.Text.Json.Nodes.JsonNode.Parse(
    """{"schemaId":"example.number/v1","schema":{"type":"number"}}""");
structuredJson["outputRequirements"] = System.Text.Json.Nodes.JsonNode.Parse(
    """[{"id":"result","quantity":"PumpFillage","unit":"%","valueSchema":{"schemaId":"example.number/v1","schema":{"type":"number"}}}]""");
var structured = await catalog.RegisterAsync(new RegisterLogicModuleCommand(structuredJson.ToJsonString(),
    new string('a', 64), LogicModuleTrustStatus.Untrusted));
var savedStructured = await catalog.GetRequiredAsync(structured.ModuleId, structured.Version);
using (var savedStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(savedStructured.ManifestJson)))
{
    var reviewed = await LogicModuleManifestReader.ReadAsync(savedStream);
    if (reviewed.DataRequirements.Single().RequiredCapability != "structured.current" ||
        reviewed.OutputRequirements!.Single().ValueSchema.SchemaId != "example.number/v1")
        throw new InvalidOperationException("Catalog lost reviewed structured declarations.");
}
structuredJson["moduleId"] = "weatherford.invalid-structured-catalog-test";
structuredJson["outputRequirements"]![0]!["valueSchema"]!["schemaId"] = "example.invalid/v1";
structuredJson["outputRequirements"]![0]!["valueSchema"]!["schema"]!["$ref"] = "https://invalid.example/schema";
try
{
    await catalog.RegisterAsync(new RegisterLogicModuleCommand(structuredJson.ToJsonString(),
        new string('b', 64), LogicModuleTrustStatus.Untrusted));
    throw new InvalidOperationException("Catalog accepted an unsupported structured schema.");
}
catch (WellSiteAutoPilotException error) when (error.Code == "MODULE_VALUE_SCHEMA_INVALID") { }
if (await repository.GetAsync("weatherford.invalid-structured-catalog-test", "1.0.0") is not null)
    throw new InvalidOperationException("Rejected structured registration wrote a catalog row.");

Console.WriteLine("Logic Module catalog persistence and immutability checks passed.");
return 0;
