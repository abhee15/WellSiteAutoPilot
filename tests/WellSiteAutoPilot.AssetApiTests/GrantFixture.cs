using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

internal static class GrantFixture
{
    // Use the published administration boundary; these tests must not seed
    // role claims or bypass paired scope validation in persistence.
    public static async Task ReplaceAsync(HttpClient administrator, Guid userId, string roleKey, Guid[] assetIds)
    {
        var path = "/api/v1/security/users/" + userId + "/grants";
        var existing = await ReadAsync(await administrator.GetAsync(path));
        foreach (var grant in existing.EnumerateArray().Where(grant => grant.GetProperty("isActive").GetBoolean()))
            await ReadAsync(await administrator.PutAsJsonAsync(path + "/" + grant.GetProperty("grantId").GetGuid() + "/deactivate",
                new { expectedRevision = grant.GetProperty("revision").GetInt64() }));
        if (assetIds.Length == 0) return;
        var roles = await ReadAsync(await administrator.GetAsync("/api/v1/security/roles?limit=500"));
        var role = roles.EnumerateArray().Single(role => role.GetProperty("key").GetString() == roleKey);
        await CreateAsync("ExactAssets", assetIds);
        if (role.GetProperty("permissions").EnumerateArray().Any(permission => permission.GetString() is "logic.read" or "logic.manage"))
            await CreateAsync("DeploymentWide", []);

        async Task CreateAsync(string scope, Guid[] ids) =>
            await ReadAsync(await administrator.PostAsJsonAsync(path, new
            {
                roleId = role.GetProperty("roleId").GetGuid(), expectedRoleRevision = role.GetProperty("revision").GetInt64(),
                scopeKind = scope, assetIds = ids
            }), HttpStatusCode.Created);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != expected)
                throw new InvalidOperationException($"Grant fixture expected {expected}, got {response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
    }
}
