using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Logic;

internal static class ConfiguredLogicApiChecks
{
    public static async Task RunAsync(HttpClient admin, HttpClient engineer,
        HttpClient unprivileged, HttpClient anonymous, Guid assetId, string typeKey)
    {
        // A trusted offline catalog fixture, never a production trust bypass.
        // HTTP module registration intentionally cannot assert its own trust.
        var connection = Environment.GetEnvironmentVariable("WSA_DATABASE_CONNECTION_STRING")!;
        var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>().UseNpgsql(connection).Options;
        await using var db = new WellSiteAutoPilotDbContext(options);
        var manifest = $$"""
        {"manifestVersion":1,"moduleId":"weatherford.setup-api-test","version":"1.0.0",
         "displayName":"Setup API Test","publisher":"Weatherford","runtime":"DotNet",
         "executionProfile":"ScheduledOneShot","entryPoint":"Weatherford.SetupTest",
         "minimumSdkVersion":"1.0.0","assetRequirements":[{"role":"well","minimumCount":1,
         "maximumCount":1,"requiredAssetTypeKey":"{{typeKey}}","requiredTraits":[]}],
         "dataRequirements":[{"id":"fillage","assetRole":"well","quantity":"PumpFillage",
         "access":"Current","canonicalUnit":"%","maximumAgeSeconds":60,"allowUncertainQuality":false}],
         "commandRequirements":[]}
        """;
        await new LogicModuleCatalogService(new LogicModuleCatalogRepository(db), TimeProvider.System)
            .RegisterAsync(new RegisterLogicModuleCommand(manifest, new string('a', 64), LogicModuleTrustStatus.TrustedPublisher));
        var reader = await Read(await unprivileged.GetAsync("/api/v1/security/me"), HttpStatusCode.OK);
        await Expect(await admin.PutAsJsonAsync("/api/v1/security/users/" + reader.GetProperty("userId").GetGuid() + "/access",
            new { roles = new List<string> { "Operator" }, assetScopeIds = Array.Empty<Guid>() }), HttpStatusCode.OK);
        var path = "/api/v1/configured-logic";
        await Expect(await anonymous.GetAsync(path + "/capabilities"), HttpStatusCode.Unauthorized);
        var caps = await Read(await engineer.GetAsync(path + "/capabilities"), HttpStatusCode.OK);
        if (!caps.GetProperty("canManage").GetBoolean()) throw new InvalidOperationException("Engineer capability missing.");
        var deniedCaps = await Read(await unprivileged.GetAsync(path + "/capabilities"), HttpStatusCode.OK);
        if (deniedCaps.GetProperty("canManage").GetBoolean()) throw new InvalidOperationException("Unprivileged management hint widened.");
        object Request(Guid bindingAsset, string parameters = "{}") => new
        {
            name = "Setup API " + Guid.NewGuid().ToString("N"), moduleManifestJson = manifest,
            parametersJson = parameters,
            assetBindings = new[] { new { role = "well", assetId = bindingAsset, parameterOverridesJson = "{}" } },
            dataBindings = new[] { new { requirementId = "fillage", assetId = bindingAsset,
                providerId = "provider-simulator", providerAssetExternalId = "WELL-101", providerMappingJson = "{\"source\":\"PumpFillage\"}" } },
            schedule = (object?)null
        };
        await Expect(await unprivileged.PostAsJsonAsync(path, Request(assetId)), HttpStatusCode.Forbidden);
        await Expect(await engineer.PostAsJsonAsync(path, Request(Guid.NewGuid())), HttpStatusCode.Forbidden);
        await Expect(await engineer.PostAsJsonAsync(path, Request(assetId, "{\"x\":1,\"x\":2}")), HttpStatusCode.BadRequest);
        await Expect(await engineer.PostAsJsonAsync(path, new { name = "bad", assetBindings = (object?)null, dataBindings = (object?)null }), HttpStatusCode.BadRequest);
        await Expect(await engineer.PostAsJsonAsync(path, new
        {
            name = "bad", moduleManifestJson = manifest, parametersJson = "{}",
            assetBindings = new object?[] { null }, dataBindings = Array.Empty<object>()
        }), HttpStatusCode.BadRequest);
        engineer.DefaultRequestHeaders.Add("X-Correlation-ID", "ci-configured-setup");
        var created = await Read(await engineer.PostAsJsonAsync(path, Request(assetId)), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        var revision = created.GetProperty("revisions")[0].GetProperty("id").GetGuid();
        var detailPath = path + "/" + id;
        var revisionPath = detailPath + "/revisions/" + revision;
        await Expect(await unprivileged.GetAsync(detailPath), HttpStatusCode.NotFound);
        await Expect(await unprivileged.PostAsync(revisionPath + "/validate", null), HttpStatusCode.Forbidden);
        await Expect(await engineer.PostAsync(revisionPath + "/activate", null), HttpStatusCode.Conflict);
        var validated = await Read(await engineer.PostAsync(revisionPath + "/validate", null), HttpStatusCode.OK);
        if (validated.GetProperty("status").GetString() != "Validated") throw new InvalidOperationException("Validation failed.");
        await Expect(await engineer.PostAsync(revisionPath + "/validate", null), HttpStatusCode.Conflict);
        async Task<HttpResponseMessage> Activate(string route, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, route);
            request.Headers.TryAddWithoutValidation("If-Match", "\"" + token + "\"");
            return await engineer.SendAsync(request);
        }
        await Expect(await Activate(revisionPath + "/activate", Guid.NewGuid().ToString()), HttpStatusCode.Conflict);
        await Expect(await Activate(revisionPath + "/activate", "none"), HttpStatusCode.OK);
        var copied = await Read(await engineer.PostAsJsonAsync(detailPath + "/revisions", Request(assetId)), HttpStatusCode.Created);
        var second = copied.GetProperty("id").GetGuid();
        var secondPath = detailPath + "/revisions/" + second;
        await Expect(await engineer.PostAsync(secondPath + "/validate", null), HttpStatusCode.OK);
        await Expect(await Activate(secondPath + "/activate", "none"), HttpStatusCode.Conflict);
        await Expect(await Activate(secondPath + "/activate", revision.ToString()), HttpStatusCode.OK);
        var persisted = await Read(await engineer.GetAsync(detailPath), HttpStatusCode.OK);
        if (persisted.GetProperty("activeRevisionId").GetGuid() != second ||
            persisted.GetProperty("revisions").EnumerateArray().Count(r => r.GetProperty("status").GetString() == "Active") != 1 ||
            persisted.GetProperty("revisions").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == revision)
                .GetProperty("status").GetString() != "Superseded")
            throw new InvalidOperationException("Immutable revision history failed roundtrip.");
        var audit = await db.AuditEvents.AsNoTracking().Where(a => a.TargetId == id.ToString()).ToArrayAsync();
        if (audit.Length != 6 || audit.Any(a => a.ActorUserId == null || a.ActorIdentity != @"LOCAL\ci-asset-engineer" ||
            a.CorrelationId != "ci-configured-setup" || a.DetailsJson.Contains("provider-simulator", StringComparison.Ordinal)))
            throw new InvalidOperationException("Mutation audit lost actor/correlation or leaked input data.");
        // Revocation is checked again by the middleware on every request.
        var me = await Read(await engineer.GetAsync("/api/v1/security/me"), HttpStatusCode.OK);
        await Expect(await admin.PutAsJsonAsync("/api/v1/security/users/" + me.GetProperty("userId").GetGuid() + "/access",
            new { roles = new List<string> { "Engineer" }, assetScopeIds = Array.Empty<Guid>() }), HttpStatusCode.OK);
        await Expect(await engineer.GetAsync(detailPath), HttpStatusCode.NotFound);
        await Expect(await engineer.PostAsJsonAsync(detailPath + "/revisions", Request(assetId)), HttpStatusCode.NotFound);
        Console.WriteLine("Live Configured Logic create/revise/validate/activate/conflict/revocation/audit checks passed.");
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode status)
    {
        using (response)
        {
            await Expect(response, status);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }
    }
    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status)
    {
        if (response.StatusCode != status)
            throw new InvalidOperationException($"Configured Logic API expected {status}, got {response.StatusCode}: " + await response.Content.ReadAsStringAsync());
    }
}
