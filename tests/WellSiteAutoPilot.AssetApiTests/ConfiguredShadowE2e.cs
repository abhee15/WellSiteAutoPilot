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
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "\"0\"");
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
        await Activate(client, path + "/activate", "none");
        var requestId = Guid.NewGuid();
        var receipts = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var response = await client.PostAsJsonAsync(path + "/shadow", new { requestId });
            if (response.StatusCode == HttpStatusCode.Conflict) return (Guid?)null;
            return (await Read(response, HttpStatusCode.OK)).GetProperty("executionId").GetGuid();
        }));
        var committedIds = receipts.Where(id => id is not null).Select(id => id!.Value).ToArray();
        if (committedIds.Length == 0 || committedIds.Distinct().Count() != 1)
            throw new InvalidOperationException("Concurrent requests lost a receipt or created multiple executions.");
        var executionId = committedIds[0];
        if ((await Send(client, path + "/shadow", requestId)).GetProperty("executionId").GetGuid() != executionId)
            throw new InvalidOperationException("Explicit replay did not recover the committed execution.");
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
        await Activate(client, failedPath + "/activate", revision.ToString());
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
        await VerifyGuidedAsync(client, assetId, pressure: false);
        using var simulator = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5091") };
        using (var reset = await simulator.PostAsJsonAsync("/api/sim/v1/scenarios/reset", new { scenario = "pressure-differential" }))
            reset.EnsureSuccessStatusCode();
        try
        {
            await VerifyGuidedAsync(client, assetId, pressure: true);
            var commands = await Read(await simulator.GetAsync("/api/sim/v1/commands/log"), HttpStatusCode.OK);
            if (commands.GetProperty("received").GetInt32() != 0)
                throw new InvalidOperationException("Guided pressure Shadow execution sent a provider command.");
        }
        finally
        {
            using var reset = await simulator.PostAsJsonAsync("/api/sim/v1/scenarios/reset", new { scenario = "rod-pump-normal" });
            reset.EnsureSuccessStatusCode();
        }
    }

    private static async Task VerifyGuidedAsync(HttpClient client, Guid assetId, bool pressure)
    {
        client.DefaultRequestHeaders.Remove("X-Correlation-ID");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "ci-guided-shadow-e2e");
        var inputs = pressure
            ? new[] { (Id: "casing-pressure", Quantity: "CasingPressure"), (Id: "tubing-pressure", Quantity: "TubingPressure") }
            : new[] { (Id: "fillage", Quantity: "PumpFillage") };
        var manifestJson = JsonSerializer.Serialize(new
        {
            manifestVersion = 1,
            moduleId = "ci.guided-" + Guid.NewGuid().ToString("N"),
            version = "1.0.0", displayName = pressure ? "Guided pressure differential" : "Guided pump fillage", publisher = "WellSite AutoPilot",
            runtime = "DotNet", executionProfile = "ScheduledOneShot",
            entryPoint = pressure ? DeclarativeThresholdModule.DifferenceEntryPoint : DeclarativeThresholdModule.EntryPoint, minimumSdkVersion = "1.1.0",
            assetRequirements = new[] { new { role = "well", minimumCount = 1, maximumCount = 1, requiredTraits = Array.Empty<string>() } },
            dataRequirements = inputs.Select(input => new { id = input.Id, assetRole = "well", quantity = input.Quantity, access = "Current",
                canonicalUnit = pressure ? "psi" : "%", maximumAgeSeconds = pressure ? 120 : 60, allowUncertainQuality = false }),
            commandRequirements = Array.Empty<object>(),
            parameterSchema = new { type = "object", properties = new { threshold = new { type = "number", minimum = 0,
                maximum = pressure ? 10000 : 100 } }, additionalProperties = false }
        });
        var draft = await Read(await client.PostAsJsonAsync("/api/v1/guided-module-drafts", new
        {
            manifestJson,
            definition = new { parameter = "threshold", @default = pressure ? 150 : 90, outcomeCode = pressure ? "PRESSURE_DIFFERENTIAL_HIGH" : "LOW_FILLAGE",
                title = pressure ? "High pressure differential" : "Low pump fillage", detail = "Review the simulator reading." }
        }), HttpStatusCode.OK);
        var draftPath = "/api/v1/guided-module-drafts/" + draft.GetProperty("id").GetGuid();
        var revision = draft.GetProperty("revision").GetInt32();
        object previewRequest = pressure
            ? new { expectedRevision = revision, pressure = new { casingPsi = 400, tubingPsi = 120 }, parametersJson = "{}" }
            : new { expectedRevision = revision, value = 82, parametersJson = "{}" };
        var preview = await Read(await client.PostAsJsonAsync(draftPath + "/preview", previewRequest), HttpStatusCode.OK);
        object submissionRequest = pressure
            ? new { expectedRevision = revision, pressureReadings = new[] { new { casingPsi = (int?)400, tubingPsi = 120 },
                new { casingPsi = (int?)270, tubingPsi = 120 }, new { casingPsi = (int?)null, tubingPsi = 120 } } }
            : new { expectedRevision = revision, readings = new int?[] { 82, 90, null } };
        var submission = await Read(await client.PostAsJsonAsync(draftPath + "/submit", submissionRequest), HttpStatusCode.OK);
        var packageSha256 = submission.GetProperty("packageSha256").GetString();
        if (preview.GetProperty("packageSha256").GetString() != packageSha256)
            throw new InvalidOperationException("The preview and submission refer to different packages.");
        if (pressure)
        {
            var review = await Read(await client.GetAsync($"{draftPath}/submissions/{revision}"), HttpStatusCode.OK);
            var cases = review.GetProperty("cases");
            if (preview.GetProperty("differentialPsi").GetDecimal() != 280 ||
                cases[0].GetProperty("pressure").GetProperty("casingPsi").GetInt32() != 400 ||
                cases[0].GetProperty("pressure").GetProperty("tubingPsi").GetInt32() != 120 ||
                cases[0].GetProperty("differentialPsi").GetDecimal() != 280 ||
                cases[1].GetProperty("differentialPsi").GetDecimal() != 150 ||
                cases[1].GetProperty("outcomeCode").GetString() != "THRESHOLD_NOT_MET" ||
                cases[2].GetProperty("failureCode").GetString() != "MODULE_DECLARATIVE_INPUT_UNAVAILABLE")
                throw new InvalidOperationException("Pressure review lost its evidence or threshold behavior: " + review);
        }
        var approved = await Read(await client.PostAsJsonAsync($"{draftPath}/submissions/{revision}/approve",
            new { packageSha256, reason = "Simulator threshold evidence reviewed" }), HttpStatusCode.OK);
        if (!approved.GetProperty("isEnabled").GetBoolean() || approved.GetProperty("packageSha256").GetString() != packageSha256)
            throw new InvalidOperationException("The reviewed package was not enabled in the catalog.");
        var moduleId = approved.GetProperty("moduleId").GetString();
        var version = approved.GetProperty("version").GetString();
        await AwaitPackagePublication(client, $"/api/v1/logic-modules/{moduleId}/{version}/package-state");

        var configuration = await Read(await client.PostAsJsonAsync("/api/v1/configured-logic", new
        {
            name = "Guided Shadow E2E", moduleManifestJson = approved.GetProperty("manifestJson").GetString(), parametersJson = "{}",
            assetBindings = new[] { new { role = "well", assetId, parameterOverridesJson = "{}" } },
            dataBindings = inputs.Select(input => new { requirementId = input.Id, assetId, providerId = "simulator",
                providerAssetExternalId = pressure ? "WELL-102" : "WELL-101", providerMappingJson = "{}" }), schedule = (object?)null
        }), HttpStatusCode.Created);
        var configurationId = configuration.GetProperty("id").GetGuid();
        var configurationRevision = configuration.GetProperty("revisions")[0].GetProperty("id").GetGuid();
        var path = $"/api/v1/configured-logic/{configurationId}/revisions/{configurationRevision}";
        await Read(await client.PostAsync(path + "/validate", null), HttpStatusCode.OK);
        await Activate(client, path + "/activate", "none");
        var receipt = await Send(client, path + "/shadow", Guid.NewGuid());
        var result = await AwaitTerminal(client, receipt.GetProperty("executionId").GetGuid());
        if (result.GetProperty("status").GetString() != "Completed" || result.GetProperty("mode").GetString() != "Shadow" ||
            result.GetProperty("correlationId").GetString() != "ci-guided-shadow-e2e" ||
            result.GetProperty("resultCode").GetString() != preview.GetProperty("outcomeCode").GetString())
            throw new InvalidOperationException("The configured worker execution diverged from the guided preview: " + result);
        using var output = JsonDocument.Parse(result.GetProperty("outputJson").GetString()!);
        var observations = output.RootElement.GetProperty("inputObservations").EnumerateArray().ToArray();
        decimal[] expected = pressure ? [120, 400] : [82];
        if (observations.Any(item => item.GetProperty("assetId").GetGuid() != assetId) ||
            !observations.Select(item => item.GetProperty("value").GetDecimal()).Order().SequenceEqual(expected) ||
            output.RootElement.GetProperty("controlIntents").GetArrayLength() != 0)
            throw new InvalidOperationException("The guided run lost input provenance or emitted a control intent.");
        if (pressure && output.RootElement.GetProperty("engineeringResults")[0].GetProperty("value").GetDecimal() != 280)
            throw new InvalidOperationException("Pressure differential diverged from preview.");
        Console.WriteLine($"Guided draft, preview, approval and configured Shadow execution passed for {(pressure ? "pressure differential" : "pump fillage")}.");
    }

    private static async Task AwaitPackagePublication(HttpClient client, string path)
    {
        JsonElement state = default;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            state = await Read(await client.GetAsync(path), HttpStatusCode.OK);
            if (state.GetProperty("publicationRecorded").GetBoolean() && state.GetProperty("objectAvailable").GetBoolean()) return;
            await Task.Delay(500);
        }
        throw new InvalidOperationException("The approved package did not reach the object store: " + state);
    }

    private static async Task Activate(HttpClient client, string path, string reviewedActiveRevision)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path);
        message.Headers.TryAddWithoutValidation("If-Match", "\"" + reviewedActiveRevision + "\"");
        await Read(await client.SendAsync(message), HttpStatusCode.OK);
    }
    private static async Task<JsonElement> Send(HttpClient client, string path, Guid requestId)
    {
        // Requests are Serializable, so a replay can lose to the worker updating the same execution's status and
        // come back as a transient conflict. The documented client behaviour is to reload and retry; a conflict that
        // persists still fails the check.
        for (var attempt = 1; ; attempt++)
        {
            var response = await client.PostAsJsonAsync(path, new { requestId });
            if (response.StatusCode != HttpStatusCode.Conflict || attempt == 6) return await Read(response, HttpStatusCode.OK);
            response.Dispose();
            await Task.Delay(250);
        }
    }
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
