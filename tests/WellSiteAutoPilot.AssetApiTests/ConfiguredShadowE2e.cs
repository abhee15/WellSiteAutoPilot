using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Logic;

internal static class ConfiguredShadowE2e
{
    public static async Task RunAsync()
    {
        var connection = Environment.GetEnvironmentVariable("WSA_DATABASE_CONNECTION_STRING")!;
        var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>().UseNpgsql(connection).Options;
        await using var db = new WellSiteAutoPilotDbContext(options);
        var typeKey = "shadow-e2e-" + Guid.NewGuid().ToString("N");
        var manifest = (await File.ReadAllTextAsync("modules/samples/sample-shadow-read.manifest.json"))
            .Replace("\"requiredAssetTypeKey\": \"well\"", $"\"requiredAssetTypeKey\": \"{typeKey}\"", StringComparison.Ordinal);
        // CI-only trusted built-in fixture. Production import does not self-approve packages.
        await new LogicModuleCatalogService(new LogicModuleCatalogRepository(db), TimeProvider.System)
            .RegisterAsync(new RegisterLogicModuleCommand(manifest, new string('b', 64), LogicModuleTrustStatus.TrustedPublisher));
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5080") };
        client.DefaultRequestHeaders.Add("X-WSA-Test-Identity", @"CI\wsa-admin");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "ci-configured-shadow-e2e");
        var type = await Read(await client.PostAsJsonAsync("/api/v1/asset-types", new
        { key = typeKey, displayName = "Configured Shadow E2E", attributeSchemaJson = "{\"type\":\"object\"}" }), HttpStatusCode.Created);
        var asset = await Read(await client.PostAsJsonAsync("/api/v1/assets", new
        { assetTypeId = type.GetProperty("id").GetGuid(), name = "Configured Shadow E2E", parentAssetId = (Guid?)null, attributeValuesJson = "{}" }), HttpStatusCode.Created);
        var assetId = asset.GetProperty("id").GetGuid();
        object Configuration(string provider) => new
        {
            name = "Configured Shadow E2E", moduleManifestJson = manifest, parametersJson = "{}",
            assetBindings = new[] { new { role = "well", assetId, parameterOverridesJson = "{}" } },
            dataBindings = new[] { new { requirementId = "pump-fillage", assetId, providerId = provider,
                providerAssetExternalId = "WELL-101", providerMappingJson = "{}" } }, schedule = (object?)null
        };
        var definition = await Read(await client.PostAsJsonAsync("/api/v1/configured-logic", Configuration("simulator")), HttpStatusCode.Created);
        var id = definition.GetProperty("id").GetGuid();
        var revision = definition.GetProperty("revisions")[0].GetProperty("id").GetGuid();
        var path = $"/api/v1/configured-logic/{id}/revisions/{revision}";
        await Read(await client.PostAsync(path + "/validate", null), HttpStatusCode.OK);
        await Read(await client.PostAsync(path + "/activate", null), HttpStatusCode.OK);
        var requestId = Guid.NewGuid();
        var receipts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Send(client, path + "/shadow", requestId)));
        var executionId = receipts[0].GetProperty("executionId").GetGuid();
        if (receipts.Any(r => r.GetProperty("executionId").GetGuid() != executionId))
            throw new InvalidOperationException("Concurrent request recovery created multiple executions.");
        var completed = await AwaitTerminal(client, executionId);
        if (completed.GetProperty("status").GetString() != "Completed" || completed.GetProperty("mode").GetString() != "Shadow")
            throw new InvalidOperationException("Configured V2 Shadow execution failed: " + completed);
        using var output = JsonDocument.Parse(completed.GetProperty("outputJson").GetString()!);
        var observation = output.RootElement.GetProperty("inputObservations")[0];
        if (observation.GetProperty("quality").GetString() != "Good" ||
            observation.GetProperty("assetId").GetGuid() != assetId ||
            observation.GetProperty("value").GetDecimal() != 82 ||
            string.IsNullOrEmpty(observation.GetProperty("source").GetString()) ||
            observation.GetProperty("timestampUtc").GetDateTimeOffset().Offset != TimeSpan.Zero ||
            output.RootElement.GetProperty("controlIntents").GetArrayLength() != 0)
            throw new InvalidOperationException("Input provenance or Shadow isolation was lost.");
        if (await db.Executions.CountAsync(e => e.Id == executionId) != 1 ||
            await db.OutboxMessages.CountAsync(m => m.Payload.Contains(executionId.ToString())) != 1 ||
            await db.AuditEvents.CountAsync(a => a.TargetId == executionId.ToString() && a.Action == "EXECUTION_SHADOW_REQUESTED") != 1 ||
            !await db.InboxMessages.AnyAsync(m => m.Consumer == "wsa-server-execution-results-v2"))
            throw new InvalidOperationException("Request/outbox/audit/inbox persistence did not remain atomic and singular.");
        var failedRevision = await Read(await client.PostAsJsonAsync($"/api/v1/configured-logic/{id}/revisions", Configuration("unavailable-provider")), HttpStatusCode.Created);
        var failedId = failedRevision.GetProperty("id").GetGuid();
        var failedPath = $"/api/v1/configured-logic/{id}/revisions/{failedId}";
        await Read(await client.PostAsync(failedPath + "/validate", null), HttpStatusCode.OK);
        await Read(await client.PostAsync(failedPath + "/activate", null), HttpStatusCode.OK);
        var recovered = await Send(client, path + "/shadow", requestId);
        if (recovered.GetProperty("executionId").GetGuid() != executionId)
            throw new InvalidOperationException("Recovery after revision drift did not return canonical execution.");
        var stale = await client.PostAsJsonAsync(path + "/shadow", new { requestId = Guid.NewGuid() });
        if (stale.StatusCode != HttpStatusCode.Conflict) throw new InvalidOperationException("Stale reviewed revision executed.");
        var failedReceipt = await Send(client, failedPath + "/shadow", Guid.NewGuid());
        var failed = await AwaitTerminal(client, failedReceipt.GetProperty("executionId").GetGuid());
        if (failed.GetProperty("status").GetString() != "Failed" || string.IsNullOrEmpty(failed.GetProperty("failureCode").GetString()))
            throw new InvalidOperationException("Provider failure was not durably inspectable.");
        Console.WriteLine("Configured Shadow V2 real HTTP/PostgreSQL/JetStream/simulator journey, request recovery, provenance and failure checks passed.");
    }
    private static async Task<JsonElement> Send(HttpClient client, string path, Guid requestId) =>
        await Read(await client.PostAsJsonAsync(path, new { requestId }), HttpStatusCode.OK);
    private static async Task<JsonElement> AwaitTerminal(HttpClient client, Guid executionId)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var result = await Read(await client.GetAsync("/api/v1/executions/" + executionId), HttpStatusCode.OK);
            if (result.GetProperty("status").GetString() is "Completed" or "Failed") return result;
            await Task.Delay(500);
        }
        throw new InvalidOperationException("Configured Shadow execution did not complete.");
    }
    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != expected) throw new InvalidOperationException($"Expected {expected}, got {response.StatusCode}: {body}");
            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
    }
}
