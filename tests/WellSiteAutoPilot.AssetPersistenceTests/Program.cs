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

// Scoped and unrestricted queries deliberately remain distinct public APIs,
// but must share identical filters, ordering and pagination mechanics.
var sibling = await service.CreateAssetAsync(
    new CreateAssetCommand(wellType.Id, "Well 102", parent.Id, "{}"));
var otherField = await service.CreateAssetAsync(
    new CreateAssetCommand(wellType.Id, "Field Bravo", null, "{}"));
var otherWell = await service.CreateAssetAsync(
    new CreateAssetCommand(wellType.Id, "Well 201", otherField.Id, "{}"));
var tankType = await service.CreateAssetTypeAsync(
    new CreateAssetTypeCommand("tank", "Tank", "{}"));
var tank = await service.CreateAssetAsync(
    new CreateAssetCommand(tankType.Id, "Tank 101", parent.Id, "{}"));

var allWells = await service.ListAssetsAsync(wellType.Id, parent.Id, 100);
if (!allWells.Select(asset => asset.Id).SequenceEqual([child.Id, sibling.Id]))
{
    throw new InvalidOperationException("Unrestricted well filtering/ordering drifted.");
}

var allowedAssets = new[] { parent.Id, child.Id, sibling.Id, tank.Id };
var scopedWells = await service.ListAssetsInScopeAsync(
    wellType.Id, parent.Id, 100, allowedAssets);
if (!scopedWells.Select(asset => asset.Id).SequenceEqual(
        allWells.Select(asset => asset.Id)))
{
    throw new InvalidOperationException("Scoped query differs from unrestricted query when fully authorized.");
}

var scopedPage = await service.ListAssetsInScopeAsync(
    wellType.Id, parent.Id, 1, allowedAssets);
var unrestrictedPage = await service.ListAssetsAsync(wellType.Id, parent.Id, 1);
if (!scopedPage.Select(asset => asset.Id).SequenceEqual(
        unrestrictedPage.Select(asset => asset.Id)))
{
    throw new InvalidOperationException("Scoped pagination differs from unrestricted pagination.");
}

var emptyScope = await service.ListAssetsInScopeAsync(
    wellType.Id, parent.Id, 100, []);
if (emptyScope.Count != 0)
{
    throw new InvalidOperationException("Empty Asset scope widened access.");
}

var partialScope = await service.ListAssetsInScopeAsync(
    wellType.Id, parent.Id, 100, [sibling.Id, otherWell.Id, tank.Id]);
if (partialScope.Count != 1 || partialScope.Single().Id != sibling.Id)
{
    throw new InvalidOperationException("Scoped type/parent filters admitted an unauthorized or unrelated Asset.");
}

var tankOnly = await service.ListAssetsInScopeAsync(
    tankType.Id, parent.Id, 100, allowedAssets);
if (tankOnly.Count != 1 || tankOnly.Single().Id != tank.Id)
{
    throw new InvalidOperationException("Scoped Asset Type filtering was not applied.");
}

var noParentMatch = await service.ListAssetsInScopeAsync(
    wellType.Id, otherField.Id, 100, [parent.Id, child.Id, sibling.Id, tank.Id]);
if (noParentMatch.Count != 0)
{
    throw new InvalidOperationException("Parent filtering leaked Assets outside the authorized scope.");
}

Console.WriteLine("Asset type and hierarchical asset persistence checks passed.");
return 0;
