using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

/// <summary>
/// The typed grant contract must keep the string wire format and reject every
/// value outside the closed set over real HTTP, never defaulting to a scope.
/// </summary>
internal static class GrantWireChecks
{
    public static async Task RunAsync(HttpClient administrator, Guid userId)
    {
        var path = "/api/v1/security/users/" + userId + "/grants";
        var roles = await ReadAsync(await administrator.GetAsync("/api/v1/security/roles?limit=500"), HttpStatusCode.OK);
        var role = roles.EnumerateArray().Single(item => item.GetProperty("key").GetString() == "engineer");
        var roleId = role.GetProperty("roleId").GetGuid().ToString();
        var revision = role.GetProperty("revision").GetInt64().ToString(CultureInfo.InvariantCulture);

        foreach (var scope in new[] { "\"Everything\"", "\"exactassets\"", "\"ALLASSETS\"", "1", "\"1\"", "0", "-1", "null", "true" })
        {
            await ExpectBadRequestAsync(administrator, path,
                "{\"roleId\":\"" + roleId + "\",\"expectedRoleRevision\":" + revision + ",\"scopeKind\":" + scope + ",\"assetIds\":[]}",
                "scopeKind " + scope);
        }
        await ExpectBadRequestAsync(administrator, path,
            "{\"roleId\":\"" + roleId + "\",\"expectedRoleRevision\":" + revision + ",\"assetIds\":[]}",
            "omitted scopeKind");

        var grants = await ReadAsync(await administrator.GetAsync(path), HttpStatusCode.OK);
        var seen = 0;
        foreach (var grant in grants.EnumerateArray())
        {
            seen++;
            if (grant.GetProperty("subjectKind").ValueKind != JsonValueKind.String ||
                grant.GetProperty("subjectKind").GetString() != "User" ||
                grant.GetProperty("scopeKind").ValueKind != JsonValueKind.String ||
                grant.GetProperty("scopeKind").GetString() is not ("ExactAssets" or "DeploymentWide" or "AllAssets"))
                throw new InvalidOperationException("Grant response left the documented string wire values: " + grant);
        }
        if (seen == 0) throw new InvalidOperationException("Expected the fixture grant to be listed.");
    }

    private static async Task ExpectBadRequestAsync(HttpClient administrator, string path, string body, string label)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await administrator.PostAsync(path, content);
        if (response.StatusCode != HttpStatusCode.BadRequest)
            throw new InvalidOperationException(
                "Grant create with " + label + " must be 400, got " + (int)response.StatusCode + ".");
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != expected)
                throw new InvalidOperationException("Grant wire check expected " + expected + ", got " + response.StatusCode + ": " + text);
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
    }
}
