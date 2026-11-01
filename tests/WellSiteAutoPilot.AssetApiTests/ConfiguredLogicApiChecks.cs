using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Logic;
using WellSiteAutoPilot.Persistence.Executions;

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
        var path = "/api/v1/configured-logic";
        await Expect(await anonymous.GetAsync(path), HttpStatusCode.Unauthorized);
        await Expect(await unprivileged.GetAsync(path), HttpStatusCode.Forbidden);
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
        await Expect(await engineer.PostAsJsonAsync(path, Request(Guid.NewGuid())), HttpStatusCode.NotFound);
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
        await Expect(await unprivileged.GetAsync(detailPath), HttpStatusCode.Forbidden);
        await Expect(await unprivileged.PostAsync(revisionPath + "/validate", null), HttpStatusCode.Forbidden);
        await Expect(await engineer.PostAsync(revisionPath + "/activate", null), HttpStatusCode.BadRequest);
        await Expect(await engineer.PutAsJsonAsync(revisionPath, Request(assetId)), HttpStatusCode.BadRequest);
        engineer.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"0\"");
        await Expect(await unprivileged.PutAsJsonAsync(revisionPath, Request(assetId)), HttpStatusCode.Forbidden);
        var edited = await Read(await engineer.PutAsJsonAsync(revisionPath, Request(assetId, "{\"edited\":true}")), HttpStatusCode.OK);
        if (edited.GetProperty("id").GetGuid() != revision || edited.GetProperty("revisionNumber").GetInt32() != 1 ||
            edited.GetProperty("draftVersion").GetInt32() != 1) throw new InvalidOperationException("Draft edit changed revision identity.");
        await Expect(await engineer.PutAsJsonAsync(revisionPath, Request(assetId)), HttpStatusCode.Conflict);
        await Expect(await engineer.PostAsync(revisionPath + "/validate", null), HttpStatusCode.Conflict);
        engineer.DefaultRequestHeaders.Remove("If-Match");
        engineer.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"1\"");
        var validated = await Read(await engineer.PostAsync(revisionPath + "/validate", null), HttpStatusCode.OK);
        await Expect(await engineer.PutAsJsonAsync(revisionPath, Request(assetId)), HttpStatusCode.Conflict);
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
        engineer.DefaultRequestHeaders.Remove("If-Match");
        engineer.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"0\"");
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
        if (audit.Length != 7 || audit.Any(a => a.ActorUserId == null || a.ActorIdentity != @"LOCAL\ci-asset-engineer" ||
            a.CorrelationId != "ci-configured-setup" || a.DetailsJson.Contains("provider-simulator", StringComparison.Ordinal)))
            throw new InvalidOperationException("Mutation audit lost actor/correlation or leaked input data.");
        var runPath = detailPath + "/revisions/" + second + "/shadow";
        await Expect(await anonymous.PostAsJsonAsync(runPath, new { requestId = Guid.NewGuid() }), HttpStatusCode.Unauthorized);
        await Expect(await unprivileged.PostAsJsonAsync(runPath, new { requestId = Guid.NewGuid() }), HttpStatusCode.Forbidden);
        await Expect(await engineer.PostAsJsonAsync(runPath, new { requestId = Guid.Empty }), HttpStatusCode.BadRequest);
        await Expect(await engineer.PostAsJsonAsync(revisionPath + "/shadow", new { requestId = Guid.NewGuid() }), HttpStatusCode.Conflict);
        var requestKey = Guid.NewGuid();
        var requests = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var response = await engineer.PostAsJsonAsync(runPath, new { requestId = requestKey });
            if (response.StatusCode == HttpStatusCode.Conflict) return (Guid?)null;
            var receipt = await Read(response, HttpStatusCode.OK);
            return receipt.GetProperty("executionId").GetGuid();
        }));
        var receipts = requests.Where(id => id is not null).Select(id => id!.Value).ToArray();
        if (receipts.Length == 0 || receipts.Distinct().Count() != 1)
            throw new InvalidOperationException("Concurrent requests lost the committed receipt or created duplicates.");
        var executionId = receipts[0];
        var activityPath = detailPath + "/activity";
        await Expect(await anonymous.GetAsync(activityPath), HttpStatusCode.Unauthorized);
        await Expect(await unprivileged.GetAsync(activityPath), HttpStatusCode.Forbidden);
        var activitySummary = await Read(await admin.GetAsync(activityPath), HttpStatusCode.OK);
        var summaryRuns = activitySummary.GetProperty("recentRuns").EnumerateArray().ToArray();
        if (activitySummary.GetProperty("activeRevisionId").GetGuid() != second || summaryRuns.Length is < 1 or > 2 ||
            !summaryRuns.Any(run => run.GetProperty("executionId").GetGuid() == executionId))
            throw new InvalidOperationException("Configuration activity lost its active revision or authorized run summary.");
        var configurationHistory = await Read(await admin.GetAsync("/api/v1/executions/history?configuredLogicId=" + id + "&limit=1"), HttpStatusCode.OK);
        if (configurationHistory.GetProperty("items").EnumerateArray().Any(run => run.GetProperty("logicInstanceId").GetGuid() != id))
            throw new InvalidOperationException("Configuration-only HTTP history crossed configuration scope.");

        var explicitReplay = await Read(await engineer.PostAsJsonAsync(runPath, new { requestId = requestKey }), HttpStatusCode.OK);
        if (explicitReplay.GetProperty("executionId").GetGuid() != executionId)
            throw new InvalidOperationException("Fresh explicit replay did not recover the committed request.");
        await Expect(await engineer.PostAsJsonAsync(runPath, new { requestId = Guid.NewGuid() }), HttpStatusCode.Conflict);
        await Expect(await engineer.PostAsJsonAsync(revisionPath + "/shadow", new { requestId = requestKey }), HttpStatusCode.Conflict);
        if (await db.OutboxMessages.CountAsync(m => m.Payload.Contains(executionId.ToString())) != 1 ||
            await db.AuditEvents.CountAsync(a => a.Action == "EXECUTION_SHADOW_REQUESTED" && a.TargetId == executionId.ToString()) != 1)
            throw new InvalidOperationException("Manual request/outbox/audit did not commit singularly.");
        var startMessage = Guid.NewGuid();
        async Task ApplyStarted()
        {
            await using var context = new WellSiteAutoPilotDbContext(options);
            await new ExecutionRepository(context).ApplyStartedAsync(startMessage, "ci-started", executionId, DateTimeOffset.UtcNow);
        }
        await Task.WhenAll(ApplyStarted(), ApplyStarted());
        var running = await Read(await engineer.GetAsync("/api/v1/executions/" + executionId), HttpStatusCode.OK);
        if (running.GetProperty("status").GetString() != "Running") throw new InvalidOperationException("Started transition was not durable.");
        var executionRepository = new ExecutionRepository(db);
        await executionRepository.ApplyCompletedAsync(Guid.NewGuid(), "ci-completed", executionId,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "CI_COMPLETED", "{}");
        var beforeLate = await Read(await engineer.GetAsync("/api/v1/executions/" + executionId), HttpStatusCode.OK);
        await executionRepository.ApplyStartedAsync(Guid.NewGuid(), "ci-late-started", executionId, DateTimeOffset.UtcNow);
        var recovered = await Read(await engineer.PostAsJsonAsync(runPath, new { requestId = requestKey }), HttpStatusCode.OK);
        if (recovered.TryGetProperty("outputJson", out _) || recovered.TryGetProperty("failureCode", out _))
            throw new InvalidOperationException("Request recovery leaked result data outside the read policy.");
        var canonical = await Read(await engineer.GetAsync("/api/v1/executions/" + executionId), HttpStatusCode.OK);
        if (canonical.GetProperty("status").GetString() != "Completed" ||
            canonical.GetProperty("startedAtUtc").GetString() != beforeLate.GetProperty("startedAtUtc").GetString() ||
            canonical.GetProperty("completedAtUtc").GetString() != beforeLate.GetProperty("completedAtUtc").GetString())
            throw new InvalidOperationException("Late started notification overwrote terminal status.");
        await db.LogicModules.Where(m => m.ModuleId == "weatherford.setup-api-test")
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.IsEnabled, false));
        await Read(await engineer.PostAsJsonAsync(runPath, new { requestId = requestKey }), HttpStatusCode.OK);
        await Expect(await engineer.PostAsJsonAsync(runPath, new { requestId = Guid.NewGuid() }), HttpStatusCode.Conflict);
        await db.LogicModules.Where(m => m.ModuleId == "weatherford.setup-api-test")
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.IsEnabled, true));
        await db.Assets.Where(a => a.Id == assetId).ExecuteUpdateAsync(set => set.SetProperty(a => a.IsActive, false));
        await Expect(await engineer.PostAsJsonAsync(runPath, new { requestId = requestKey }), HttpStatusCode.Forbidden);
        await Expect(await engineer.PostAsJsonAsync(runPath, new { requestId = Guid.NewGuid() }), HttpStatusCode.Forbidden);
        await db.Assets.Where(a => a.Id == assetId).ExecuteUpdateAsync(set => set.SetProperty(a => a.IsActive, true));
        // Revocation is checked again by the middleware on every request.
        var me = await Read(await engineer.GetAsync("/api/v1/security/me"), HttpStatusCode.OK);
        await GrantFixture.ReplaceAsync(admin, me.GetProperty("userId").GetGuid(), "engineer", []);
        await Expect(await engineer.GetAsync(detailPath), HttpStatusCode.Forbidden);
        await Expect(await engineer.PostAsJsonAsync(runPath, new { requestId = requestKey }), HttpStatusCode.Forbidden);
        await Expect(await engineer.GetAsync("/api/v1/executions/" + executionId), HttpStatusCode.Forbidden);
        await Expect(await engineer.PostAsJsonAsync(detailPath + "/revisions", Request(assetId)), HttpStatusCode.Forbidden);
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
