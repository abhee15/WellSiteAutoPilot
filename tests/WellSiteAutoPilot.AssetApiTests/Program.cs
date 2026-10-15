using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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
if (!adminMe.GetProperty("roles").EnumerateArray()
    .Any(x => x.GetString() == "Admin"))
    throw new InvalidOperationException("CI bootstrap principal has no Admin role");

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
await CheckAsync(await admin.PutAsJsonAsync(
    "/api/v1/security/users/" + engineerId + "/access",
    new { roles = new[] { "Engineer" }, assetScopeIds = new[] { fieldId } }),
    HttpStatusCode.OK, "Grant scoped Engineer access");

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
}), HttpStatusCode.Forbidden, "Engineer must not create under unscoped parent");

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
    HttpStatusCode.Forbidden, "Child must not leak via parent-only scope");
var engineerAssets = await BodyAsync(
    await engineer.GetAsync("/api/v1/assets?limit=100"), HttpStatusCode.OK);
if (!engineerAssets.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == fieldId) ||
    engineerAssets.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == childId))
    throw new InvalidOperationException("List filtering violated explicit Asset scope");

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
    using (response)
    {
        if (response.StatusCode == expected) return;
        var payload = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException(
            $"{operation}: expected {(int)expected}, got {(int)response.StatusCode}. " +
            payload[..Math.Min(payload.Length, 512)]);
    }
}
