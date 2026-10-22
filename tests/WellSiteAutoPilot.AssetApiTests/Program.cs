using Npgsql;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

// Isolate the real HTTP suite from service tests' provisioned administrators.
// No reset/drop or production bootstrap bypass is involved.
if (args.Length == 1 && args[0] == "--prepare")
{
    var testConnection = new NpgsqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("WSA_DATABASE_CONNECTION_STRING") ??
        throw new InvalidOperationException("WSA_DATABASE_CONNECTION_STRING is required."));
    if (testConnection.Database != "wsa_asset_api")
        throw new InvalidOperationException("Asset API preparation requires its dedicated wsa_asset_api test database.");
    testConnection.Database = "postgres";
    await using var connection = new NpgsqlConnection(testConnection.ConnectionString);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("CREATE DATABASE wsa_asset_api", connection);
    await command.ExecuteNonQueryAsync();
    return 0;
}
if (args.Length == 1 && args[0] == "--configured-e2e")
{
    await ConfiguredShadowE2e.RunAsync();
    return 0;
}
if (args.Length == 1 && args[0] == "--v1-fixture")
{
    await LegacyShadowFixture.RunAsync();
    return 0;
}
if (args.Length != 0) throw new InvalidOperationException("Unsupported Asset API test arguments.");

var origin = Environment.GetEnvironmentVariable("WSA_ASSET_API_URL") ??
    throw new InvalidOperationException("WSA_ASSET_API_URL is required.");
using var admin = Client(@"LOCAL\ci-asset-admin");
using var engineer = Client(@"LOCAL\ci-asset-engineer");
using var operatorUser = Client(@"LOCAL\ci-asset-operator");
using var anonymous = new HttpClient { BaseAddress = new Uri(origin) };

// Test genuine HTTP authorization middleware and EF-backed server, not a
// client mock or a direct repository call. CI uses a disposable PostgreSQL DB.
await CheckAsync(await anonymous.GetAsync("/api/v1/assets/capabilities"),
    HttpStatusCode.Unauthorized, "Missing identity must challenge");
var adminMe = await BodyAsync(await admin.GetAsync("/api/v1/security/me"),
    HttpStatusCode.OK);
if (!adminMe.GetProperty("permissions").EnumerateArray().Any(item => item.GetString() == "security.manage") ||
    adminMe.TryGetProperty("roles", out _) || adminMe.TryGetProperty("assetScopeIds", out _))
    throw new InvalidOperationException("Bootstrap session must expose grant hints without legacy role/scope unions.");

var key = "ci-well-" + Guid.NewGuid().ToString("N");
var schema = """{"type":"object","required":["depth"],"properties":{"depth":{"type":"number","minimum":0}},"additionalProperties":false}""";
var assetType = await BodyAsync(await admin.PostAsJsonAsync(
    "/api/v1/asset-types",
    new { key, displayName = "CI Well", attributeSchemaJson = schema }),
    HttpStatusCode.Created);
var typeId = assetType.GetProperty("id").GetGuid();

var adminCaps = await BodyAsync(
    await admin.GetAsync("/api/v1/assets/capabilities"), HttpStatusCode.OK);
if (!adminCaps.GetProperty("canCreateRoot").GetBoolean() ||
    !adminCaps.GetProperty("canCreateUnderAuthorizedParents").GetBoolean())
    throw new InvalidOperationException("Administrator creation capabilities missing");

await CheckAsync(await admin.PostAsJsonAsync("/api/v1/assets", new
{
    assetTypeId = typeId, name = "Invalid without depth",
    parentAssetId = (Guid?)null, attributeValuesJson = "{}"
}), HttpStatusCode.BadRequest, "Missing required schema field must reject");

var name = "CI Field " + Guid.NewGuid().ToString("N")[..8];
var field = await BodyAsync(await admin.PostAsJsonAsync("/api/v1/assets", new
{
    assetTypeId = typeId, name, parentAssetId = (Guid?)null,
    attributeValuesJson = """{"depth":1200.5}"""
}), HttpStatusCode.Created);
var fieldId = field.GetProperty("id").GetGuid();
var readback = await BodyAsync(
    await admin.GetAsync("/api/v1/assets/" + fieldId), HttpStatusCode.OK);
if (readback.GetProperty("assetTypeId").GetGuid() != typeId ||
    readback.GetProperty("name").GetString() != name)
    throw new InvalidOperationException("Admin create did not round-trip through API");

// Resolve a second real user, then assign scoped Engineer access as Admin.
var engineerMe = await BodyAsync(
    await engineer.GetAsync("/api/v1/security/me"), HttpStatusCode.OK);
var engineerId = engineerMe.GetProperty("userId").GetGuid();
await GrantFixture.ReplaceAsync(admin, engineerId, "engineer", [fieldId]);
await GrantWireChecks.RunAsync(admin, engineerId);

var engineerCaps = await BodyAsync(
    await engineer.GetAsync("/api/v1/assets/capabilities"), HttpStatusCode.OK);
if (engineerCaps.GetProperty("canCreateRoot").GetBoolean() ||
    !engineerCaps.GetProperty("canCreateUnderAuthorizedParents").GetBoolean())
    throw new InvalidOperationException("Engineer capabilities widened root scope");

await CheckAsync(await engineer.PostAsJsonAsync("/api/v1/assets", new
{
    assetTypeId = typeId, name = "Forbidden root",
    parentAssetId = (Guid?)null, attributeValuesJson = """{"depth":1}"""
}), HttpStatusCode.Forbidden, "Engineer must not create root");

await CheckAsync(await engineer.PostAsJsonAsync("/api/v1/assets", new
{
    assetTypeId = typeId, name = "Forbidden parent",
    parentAssetId = Guid.NewGuid(), attributeValuesJson = """{"depth":1}"""
}), HttpStatusCode.NotFound, "Engineer must not create under missing parent");

var createdChild = await BodyAsync(await engineer.PostAsJsonAsync("/api/v1/assets",
    new
    {
        assetTypeId = typeId, name = "CI child " + Guid.NewGuid().ToString("N")[..8],
        parentAssetId = fieldId, attributeValuesJson = """{"depth":2200}"""
    }), HttpStatusCode.Created);
var childId = createdChild.GetProperty("id").GetGuid();

// Today's explicit scope does not imply descendant scope (#65). This is
// intentionally denied, even after the Engineer created the child. The UI
// reports confirmed creation with pending administrator-granted visibility.
await CheckAsync(await engineer.GetAsync("/api/v1/assets/" + childId),
    HttpStatusCode.NotFound, "Child must not leak via parent-only scope");
var engineerAssets = await BodyAsync(
    await engineer.GetAsync("/api/v1/assets?limit=100"), HttpStatusCode.OK);
if (!engineerAssets.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == fieldId) ||
    engineerAssets.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == childId))
    throw new InvalidOperationException("List filtering violated explicit Asset scope");

// Browsing a large hierarchy: children with counts and search with locations obey the same scope.
var childName = createdChild.GetProperty("name").GetString()!;
var adminRoots = await BodyAsync(
    await admin.GetAsync("/api/v1/assets/children?limit=500"), HttpStatusCode.OK);
var adminField = adminRoots.EnumerateArray().SingleOrDefault(x => x.GetProperty("id").GetGuid() == fieldId);
if (adminField.ValueKind != JsonValueKind.Object || adminField.GetProperty("childCount").GetInt32() < 1)
    throw new InvalidOperationException("Top level must list the field with its child count");
var adminChildren = await BodyAsync(
    await admin.GetAsync("/api/v1/assets/children?parentAssetId=" + fieldId), HttpStatusCode.OK);
if (!adminChildren.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == childId))
    throw new InvalidOperationException("Children of the field must include the new child");
var engineerRoots = await BodyAsync(
    await engineer.GetAsync("/api/v1/assets/children?limit=500"), HttpStatusCode.OK);
var engineerField = engineerRoots.EnumerateArray().SingleOrDefault(x => x.GetProperty("id").GetGuid() == fieldId);
if (engineerField.ValueKind != JsonValueKind.Object || engineerField.GetProperty("childCount").GetInt32() != 0)
    throw new InvalidOperationException("Child counts must not count assets outside the caller's scope");
var engineerChildren = await BodyAsync(
    await engineer.GetAsync("/api/v1/assets/children?parentAssetId=" + fieldId), HttpStatusCode.OK);
if (engineerChildren.GetArrayLength() != 0)
    throw new InvalidOperationException("Children outside the caller's scope leaked");
var adminSearch = await BodyAsync(
    await admin.GetAsync("/api/v1/assets/search?q=" + Uri.EscapeDataString(childName[..8].ToUpperInvariant())),
    HttpStatusCode.OK);
var adminHit = adminSearch.EnumerateArray().SingleOrDefault(x => x.GetProperty("id").GetGuid() == childId);
if (adminHit.ValueKind != JsonValueKind.Object ||
    adminHit.GetProperty("path").GetArrayLength() != 1 ||
    adminHit.GetProperty("path")[0].GetProperty("id").GetGuid() != fieldId)
    throw new InvalidOperationException("Search must find the child case-insensitively with its location");
var engineerSearch = await BodyAsync(
    await engineer.GetAsync("/api/v1/assets/search?q=" + Uri.EscapeDataString(childName[..8])), HttpStatusCode.OK);
if (engineerSearch.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == childId))
    throw new InvalidOperationException("Search leaked an asset outside the caller's scope");
var engineerScopedSearch = await BodyAsync(
    await engineer.GetAsync("/api/v1/assets/search?standalone=true&q=" + Uri.EscapeDataString(childName[..8])),
    HttpStatusCode.OK);
if (engineerScopedSearch.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == childId))
    throw new InvalidOperationException("An owner filter must never widen Asset authorization");
var foreignConnectionChildren = await BodyAsync(
    await admin.GetAsync("/api/v1/assets/children?parentAssetId=" + fieldId + "&ownerConnectionId=" + Guid.NewGuid()),
    HttpStatusCode.OK);
if (foreignConnectionChildren.GetArrayLength() != 0)
    throw new InvalidOperationException("Owner scope must also apply below the top level");
var wildcardSearch = await BodyAsync(
    await admin.GetAsync("/api/v1/assets/search?q=" + Uri.EscapeDataString("%")), HttpStatusCode.OK);
if (wildcardSearch.GetArrayLength() != 0)
    throw new InvalidOperationException("Search text must match literally, not as a wildcard");
await CheckAsync(await admin.GetAsync("/api/v1/assets/search"),
    HttpStatusCode.BadRequest, "Search without text must be rejected");
await CheckAsync(await admin.GetAsync("/api/v1/assets/children?limit=0"),
    HttpStatusCode.BadRequest, "A zero page size must be rejected");

await CheckAsync(await operatorUser.PostAsJsonAsync("/api/v1/assets", new
{
    assetTypeId = typeId, name = "Operator denied",
    parentAssetId = fieldId, attributeValuesJson = "{}"
}), HttpStatusCode.Forbidden, "Unprivileged operator must be denied");

await CheckAsync(await admin.PostAsJsonAsync("/api/v1/asset-types", new
{
    key = "unsafe-" + Guid.NewGuid().ToString("N"),
    displayName = "Unsafe reference",
    attributeSchemaJson = """{"$ref":"https://example.org/remote-schema.json"}"""
}), HttpStatusCode.BadRequest, "Remote JSON Schema reference must be rejected");

await ConfiguredLogicApiChecks.RunAsync(admin, engineer, operatorUser, anonymous, fieldId, key);

await ChangeSignalWireChecks.RunAsync(new Uri(origin), anonymous, admin, @"LOCAL\ci-asset-admin", @"LOCAL\ci-asset-operator");

Console.WriteLine("Live Asset API create/capabilities/schema/scope checks passed.");
return 0;

HttpClient Client(string identity)
{
    var client = new HttpClient { BaseAddress = new Uri(origin) };
    client.DefaultRequestHeaders.Add("X-WSA-Test-Identity", identity);
    return client;
}

static async Task<JsonElement> BodyAsync(
    HttpResponseMessage response, HttpStatusCode expected)
{
    using (response)
    {
        await CheckAsync(response, expected, "API read");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.Clone();
    }
}

static async Task CheckAsync(
    HttpResponseMessage response, HttpStatusCode expected, string operation)
{
    // BodyAsync retains ownership until it has parsed the payload.
    if (response.StatusCode == expected) return;
    var payload = await response.Content.ReadAsStringAsync();
    throw new InvalidOperationException(
        $"{operation}: expected {(int)expected}, got {(int)response.StatusCode}. " +
        payload[..Math.Min(payload.Length, 512)]);
}
