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

// Administration must discover the full asset directory in deterministic
// pages even when multiple Assets have exactly the same display name.
var duplicateName = await service.CreateAssetAsync(
    new CreateAssetCommand(wellType.Id, "Well 101", parent.Id, "{}"));
var directoryExpected = await dbContext.Assets.AsNoTracking()
    .OrderBy(item => item.Name).ThenBy(item => item.Id)
    .Select(item => item.Id).ToArrayAsync();
if (!directoryExpected.Contains(duplicateName.Id) ||
    !directoryExpected.Contains(child.Id))
{
    throw new InvalidOperationException("Created duplicate-name Assets are missing.");
}
var directoryIds = new List<Guid>();
for (var offset = 0; ; offset += 2)
{
    var page = await service.ListAssetDirectoryPageAsync(offset, 2);
    directoryIds.AddRange(page.Select(item => item.Id));
    if (page.Count < 2) break;
}
if (!directoryIds.SequenceEqual(directoryExpected) ||
    directoryIds.Distinct().Count() != directoryExpected.Length)
{
    throw new InvalidOperationException(
        "Paged administrator Asset directory skipped, duplicated or reordered Assets.");
}
try
{
    await service.ListAssetDirectoryPageAsync(-1, 200);
    throw new InvalidOperationException("Negative Asset directory offset was accepted.");
}
catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException exception)
    when (exception.Code == WellSiteAutoPilot.Failures.FailureCodes.ValidationFailed)
{
}
try
{
    await service.ListAssetDirectoryPageAsync(0, 501);
    throw new InvalidOperationException("Unbounded Asset directory page was accepted.");
}
catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException exception)
    when (exception.Code == WellSiteAutoPilot.Failures.FailureCodes.ValidationFailed)
{
}

// Standard JSON Schema subset is enforced by the server *before* PostgreSQL
// persistence. Reject missing/wrong/out-of-range/unknown attributes even if
// they are syntactically valid JSON.
var depthType = await service.CreateAssetTypeAsync(
    new CreateAssetTypeCommand(
        "well-depth", "Well with measured depth",
        """{"type":"object","required":["depth"],"properties":{"depth":{"type":"number","minimum":0},"name":{"type":"string","minLength":2}},"additionalProperties":false}"""));
var measuredWell = await service.CreateAssetAsync(
    new CreateAssetCommand(depthType.Id, "Measured Well", parent.Id,
        """{"depth":250.5,"name":"A1"}"""));
if ((await service.GetRequiredAssetAsync(measuredWell.Id)).Name != "Measured Well")
{
    throw new InvalidOperationException("Schema-valid Asset was not persisted.");
}

foreach (var invalid in new[]
{
    "{}",
    """{"depth":"250"}""",
    """{"depth":-1}""",
    """{"depth":200,"extra":"not defined"}""",
    """{"depth":200,"name":"A"}""",
    """{"depth":200,"depth":300}""",
    """{"depth":200,"nested":{"code":"X","code":"Y"}}""",
    """["not an object"]""",
    "{this is not JSON}"
})
{
    await AssertFailureAsync(
        () => service.CreateAssetAsync(
            new CreateAssetCommand(depthType.Id, "Rejected", parent.Id, invalid)),
        "ASSET_ATTRIBUTE_VALUES_INVALID");
}

// Reject schema keywords not included in the reviewed V1 offline profile;
// especially prevent file/HTTP reference resolution.
foreach (var unsafeSchema in new[]
{
    """{"$ref":"https://example.com/evil.json"}""",
    """{"type":"object","properties":{"x":{"$ref":"file:///etc/passwd"}}}""",
    """{"type":"object","allOf":[{"type":"object"}]}""",
    """{"type":"object","type":"object"}""",
    """{"type":"object","properties":{"x":{"type":"string"},"x":{"type":"number"}}}""",
    """{"type":"object","properties":{"x":{"type":"string","pattern":"(a+)+"}}}""",
    """{"type":"array"}""",
    """{"type":"object","properties":{"x":{"type":"mystery"}}}""",
    """{"type":"object","additionalProperties":{"type":"string"}}"""
})
{
    await AssertFailureAsync(
        () => service.CreateAssetTypeAsync(
            new CreateAssetTypeCommand("unsafe-" + Guid.NewGuid(), "Unsafe", unsafeSchema)),
        "ASSET_ATTRIBUTE_SCHEMA_UNSUPPORTED");
}

await AssertFailureAsync(
    () => service.CreateAssetTypeAsync(
        new CreateAssetTypeCommand("invalid-json", "Invalid", "{invalid")),
    "ASSET_ATTRIBUTE_SCHEMA_INVALID");
await AssertFailureAsync(
    () => service.CreateAssetTypeAsync(
        new CreateAssetTypeCommand("huge-json", "Huge",
            new string(' ', 32769) + "{}")),
    "ASSET_ATTRIBUTE_SCHEMA_INVALID");
await AssertFailureAsync(
    () => service.CreateAssetAsync(
        new CreateAssetCommand(depthType.Id, "Huge", parent.Id,
            new string(' ', 65537) + "{}")),
    "ASSET_ATTRIBUTE_VALUES_INVALID");

// Active Asset Type and parent lifecycle checks are enforced independently
// of client-side list filtering.
var disabledType = await service.CreateAssetTypeAsync(
    new CreateAssetTypeCommand("disabled-type", "Disabled Type", "{}"));
var trackedDisabledType = await dbContext.AssetTypes.SingleAsync(
    x => x.Id == disabledType.Id);
trackedDisabledType.IsActive = false;
await dbContext.SaveChangesAsync();
await AssertFailureAsync(
    () => service.CreateAssetAsync(
        new CreateAssetCommand(disabledType.Id, "Blocked inactive type", parent.Id, "{}")),
    "ASSET_TYPE_NOT_FOUND");

var trackedParent = await dbContext.Assets.SingleAsync(x => x.Id == parent.Id);
trackedParent.IsActive = false;
await dbContext.SaveChangesAsync();
await AssertFailureAsync(
    () => service.CreateAssetAsync(
        new CreateAssetCommand(wellType.Id, "Blocked inactive parent", parent.Id, "{}")),
    "PARENT_ASSET_NOT_FOUND");
trackedParent.IsActive = true;
await dbContext.SaveChangesAsync();

// The schema path must validate old rows as well as newly created types.
var legacyType = await service.CreateAssetTypeAsync(
    new CreateAssetTypeCommand("legacy-row", "Legacy Row", "{}"));
var trackedType = await dbContext.AssetTypes.SingleAsync(x => x.Id == legacyType.Id);
trackedType.AttributeSchemaJson = """{"$ref":"https://example.com/must-not-fetch"}""";
await dbContext.SaveChangesAsync();
await AssertFailureAsync(
    () => service.CreateAssetAsync(
        new CreateAssetCommand(legacyType.Id, "Blocked legacy row", parent.Id, "{}")),
    "ASSET_ATTRIBUTE_SCHEMA_UNSUPPORTED");


await HierarchyPersistenceChecks.RunAsync(options, wellType.Id);

await AssetViewChecks.RunAsync(options, wellType.Id, tankType.Id);

Console.WriteLine("Asset type, hierarchical asset and explicit View persistence checks passed.");
return 0;


static async Task AssertFailureAsync(Func<Task> operation, string expectedCode)
{
    try
    {
        await operation();
    }
    catch (WellSiteAutoPilot.Failures.WellSiteAutoPilotException ex)
    {
        if (ex.Code == expectedCode) return;
        throw new InvalidOperationException(
            $"Expected failure {expectedCode}, observed {ex.Code}.", ex);
    }
    throw new InvalidOperationException($"Expected {expectedCode}, operation succeeded.");
}
