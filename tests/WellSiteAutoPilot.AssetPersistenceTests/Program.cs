using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;

var connectionString = Environment.GetEnvironmentVariable("WSA_DATABASE_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("WSA_DATABASE_CONNECTION_STRING is required.");
}

var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var dbContext = new WellSiteAutoPilotDbContext(options);
var repository = new AssetRepository(dbContext);
var service = new AssetService(repository, TimeProvider.System);

var wellType = await service.CreateAssetTypeAsync(
    new CreateAssetTypeCommand(
        "well",
        "Well",
        """{"type":"object","properties":{"field":{"type":"string"}}}"""));

var parent = await service.CreateAssetAsync(
    new CreateAssetCommand(
        wellType.Id,
        "Field Alpha",
        null,
        """{"field":"Alpha"}"""));

var child = await service.CreateAssetAsync(
    new CreateAssetCommand(
        wellType.Id,
        "Well 101",
        parent.Id,
        """{"field":"Alpha"}"""));

var typeRoundTrip = await service.GetRequiredAssetTypeAsync(wellType.Id);
var assetRoundTrip = await service.GetRequiredAssetAsync(child.Id);
var listed = await service.ListAssetsAsync(wellType.Id, parent.Id, 100);

if (typeRoundTrip.Key != "well" ||
    assetRoundTrip.Name != "Well 101" ||
    assetRoundTrip.ParentAssetId != parent.Id ||
    listed.Count != 1 ||
    listed.Single().Id != child.Id)
{
    throw new InvalidOperationException("Asset persistence contract did not round-trip correctly.");
}

Console.WriteLine("Asset type and hierarchical asset persistence checks passed.");
return 0;
