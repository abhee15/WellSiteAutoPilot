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

Console.WriteLine("Logic Module catalog persistence and immutability checks passed.");
return 0;
