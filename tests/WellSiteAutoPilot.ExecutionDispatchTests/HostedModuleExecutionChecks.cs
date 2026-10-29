using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.IntegrationGateway.Providers;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Infrastructure.Messaging;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Logic;
using WellSiteAutoPilot.Persistence.Security;
using WellSiteAutoPilot.Worker.DotNet.Execution;

namespace WellSiteAutoPilot.ExecutionDispatchTests;

internal static class HostedModuleExecutionChecks
{
    private static readonly int[] CardPointCounts = [400, 112];
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, NatsConnection connection,
        bool savedConnection = false, bool disabledConnection = false, string? foreSitePayload = null, bool structured = false, string structuredQuality = "Good")
    {
        await using var db = new WellSiteAutoPilotDbContext(options);
        var structuredRejected = structured && structuredQuality == "Unknown";
        var now = DateTimeOffset.UtcNow;
        var user = Guid.NewGuid();
        var moduleId = "hosted-probe-" + user.ToString("N");
        var actor = new SecurityActorContext(user, moduleId, moduleId);
        var subject = new AccessSubject(user);
        db.Users.Add(new UserEntity { Id = user, IdentityKey = moduleId, IdentityName = moduleId,
            NormalizedIdentityName = moduleId, IsActive = true, CreatedAtUtc = now, LastSeenAtUtc = now });
        var role = Guid.NewGuid();
        db.RoleDefinitions.Add(new RoleDefinitionEntity { Id = role, Key = moduleId, Name = moduleId,
            NormalizedName = moduleId, IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.RolePermissions.Add(new RolePermissionEntity { RoleId = role, Permission = WellSitePermissions.LogicManage });
        db.AccessGrants.Add(new AccessGrantEntity { Id = Guid.NewGuid(), UserId = user, RoleId = role,
            SubjectKind = "User", ScopeKind = "DeploymentWide", IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync();
        var manifest = new LogicModuleManifest(1, moduleId, "1.0.0", "Hosted probe", "Test", LogicRuntimeKind.DotNet,
            ExecutionProfile.ScheduledOneShot, typeof(HostedExecutionProbe).FullName!, "1.1.0",
            [new("well", 1, 1, null, [])], [new("fillage", "well", "PumpFillage", "Current", "%", 60, false)], []);
        if (structured)
            manifest = manifest with
            {
                ManifestVersion = 2, MinimumSdkVersion = "1.2.0", EntryPoint = typeof(HostedStructuredProbe).FullName!,
                DataRequirements = [new("fillage", "well", "SurfaceCards", "Current", null, 60, false,
                    new("foresite.surface-cards/v1", CardSchema()), "structured.current")],
                OutputRequirements = [new("curve", "Curve", null,
                    new("fixture.curve/v1", CardSchema()))]
            };
        using var bytes = new MemoryStream(Pack(manifest));
        var module = await new LogicModulePackageService(new LogicModuleCatalogRepository(db), TimeProvider.System)
            .UploadAsync(bytes, subject, actor);
        var asset = Guid.NewGuid();
        var command = new ConfiguredShadowExecutionCommand(Guid.NewGuid(), Guid.NewGuid(), moduleId, "1.0.0", "{}",
            [new("well", asset, "{}")], [new("fillage", asset, "simulator", "WELL-PROBE", "PumpFillage", "Current", "%", 60, false, "{}")],
            ExecutionTriggerKind.Scheduled, now);
        if (structured)
            command = command with { Inputs = [command.Inputs.Single() with
            { Quantity = "SurfaceCards", CanonicalUnit = null, RequiredCapability = "structured.current", SchemaId = "foresite.surface-cards/v1" }] };
        var connectionId = Guid.NewGuid();
        if (savedConnection)
        {
            db.RolePermissions.AddRange(new RolePermissionEntity { RoleId = role, Permission = WellSitePermissions.ExecutionsRequest },
                new RolePermissionEntity { RoleId = role, Permission = WellSitePermissions.ConfiguredLogicRead });
            db.AccessGrants.Add(new() { Id = Guid.NewGuid(), UserId = user, RoleId = role, SubjectKind = "User", ScopeKind = "AllAssets",
                IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            var type = Guid.NewGuid();
            db.AssetTypes.Add(new() { Id = type, Key = moduleId, DisplayName = moduleId, AttributeSchemaJson = "{\"type\":\"object\"}",
                SchemaVersion = 1, IsActive = true, CreatedAtUtc = now });
            db.Connections.Add(new() { Id = connectionId, Name = moduleId, NormalizedName = moduleId, AdapterKey = "foresite", SettingsVersion = 1,
                SettingsJson = "{\"baseUrl\":\"http://private-fixture/prefix/\"}", Revision = 1, IsEnabled = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.Assets.Add(new() { Id = asset, AssetTypeId = type, Name = moduleId, OwnerConnectionId = connectionId, IsActive = true, CreatedAtUtc = now });
            db.SourceBindings.Add(new() { Id = Guid.NewGuid(), AssetId = asset, ConnectionId = connectionId, ExternalId = "42",
                ResourceKind = "foresite.well", CreatedAtUtc = now });
            db.ConfiguredLogicDefinitions.Add(new() { Id = command.ConfiguredLogicId, Name = moduleId, ActiveRevisionId = command.ConfigurationRevisionId, CreatedAtUtc = now });
            db.ConfiguredLogicRevisions.Add(new() { Id = command.ConfigurationRevisionId, ConfiguredLogicId = command.ConfiguredLogicId, RevisionNumber = 1,
                ModuleId = moduleId, ModuleVersion = "1.0.0", Mode = "Shadow", Status = "Active", CreatedAtUtc = now });
            await db.SaveChangesAsync();
            command = command with { Inputs = [command.Inputs.Single() with { ProviderId = "foresite", ProviderAssetExternalId = "42" }],
                Trigger = ExecutionTriggerKind.Manual, ScheduledForUtc = null };
        }
        else await RefuseDispatchAsync("MODULE_NOT_ENABLED");
        var approval = new LogicModuleApprovalService(new LogicModuleCatalogRepository(db));
        await approval.ApproveAsync(moduleId, "1.0.0", "Reviewed executable fixture", subject, actor);
        await new ModulePackagePublisher(db, new NatsModulePackageObjectStore(connection), TimeProvider.System,
            NullLogger<ModulePackagePublisher>.Instance).DispatchAsync(module.Id);

        var execution = savedConnection
            ? await new ExecutionRepository(db).AddManualConfiguredRequestedAsync(
                ExecutionService.CreateConfiguredRequest(command, moduleId, now, Guid.NewGuid()), command, actor)
            : await new ExecutionService(new ExecutionRepository(db), TimeProvider.System).RequestConfiguredShadowAsync(command, moduleId);
        var hosted = JsonSerializer.Deserialize<ExecutionRequestedV3>(execution.RequestPayloadJson!, Options)!;
        Assert(execution.RequestContractVersion == 3 && hosted.Package.Sha256 == module.PackageSha256 &&
            hosted.Package.ReviewedManifestJson == module.ManifestJson && hosted.Execution.ExecutionId == execution.Id,
            "Dispatch did not snapshot the authoritative approved package.");
        // Retirement stops NEW dispatch. A committed request retains its reviewed bytes.
        await approval.RetireAsync(moduleId, "1.0.0", "Exercise accepted-request boundary", subject, actor);
        if (!savedConnection) await RefuseDispatchAsync("MODULE_NOT_ENABLED");
        if (disabledConnection) await db.Connections.Where(c => c.Id == connectionId).ExecuteUpdateAsync(set => set.SetProperty(c => c.IsEnabled, false));

        var store = await connection.CreateObjectStoreContext().GetObjectStoreAsync("WSA_MODULES");
        using (var corrupt = new MemoryStream([1, 2, 3]))
            await store.PutAsync("sha256/" + new string('f', 64), corrupt);
        var working = Path.Combine(Path.GetTempPath(), "wsa-hosted-e2e-" + user.ToString("N"));
        Environment.SetEnvironmentVariable("WSA_HOSTED_EXECUTION_TEST_ID", moduleId);
        using var http = new HttpClient(new ProbeData(now)) { BaseAddress = new Uri("http://gateway.invalid") };
        var fixture = new ForeSiteHttpFixture(foreSitePayload, now);
        using var measurements = new ForeSiteConnectionMeasurements(fixture, TimeProvider.System);
        var services = new ServiceCollection().AddScoped(_ => new WellSiteAutoPilotDbContext(options))
            .AddScoped<IExecutionReadRepository>(provider => new ExecutionRepository(provider.GetRequiredService<WellSiteAutoPilotDbContext>()))
            .AddSingleton<IExecutionMeasurementGateway>(new RecordedMeasurements(connectionId, now, foreSitePayload is null ? null : measurements, structured, structuredQuality));
        await using var readProvider = services.BuildServiceProvider();
        using var reads = new ExecutionMeasurementReadConsumer(readProvider.GetRequiredService<IServiceScopeFactory>(), connection,
            NullLogger<ExecutionMeasurementReadConsumer>.Instance);
        var engine = new ModuleExecutionEngine(new GatewayDataClient(http, connection), new BuiltInLogicModuleResolver(),
            packageHost: new DotNetModulePackageHost(new NatsModulePackageSource(connection), working));
        using var worker = new ExecutionRequestV2Consumer(connection, engine, TimeProvider.System,
            new JetStreamConsumerSettings(), NullLogger<ExecutionRequestV2Consumer>.Instance);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var results = await connection.CreateJetStreamContext().CreateOrUpdateConsumerAsync(JetStreamTopology.ExecutionStream,
            new ConsumerConfig("hosted-results-" + user.ToString("N"))
            { FilterSubjects = [Subjects.ExecutionStartedV2, Subjects.ExecutionCompletedV2, Subjects.ExecutionFailedV2] }, deadline.Token);
        var sawStarted = false;
        var sawCompleted = false;
        try
        {
            if (savedConnection) await reads.StartAsync(deadline.Token);
            await worker.StartAsync(deadline.Token);
            var publisher = new OutboxPublisher(db, connection, TimeProvider.System);
            for (var batch = 0; batch < 10 && !await db.OutboxMessages.AsNoTracking().AnyAsync(item =>
                item.Type == Subjects.ExecutionRequestedV3 && item.Payload.Contains(execution.Id.ToString()) && item.ProcessedAtUtc != null); batch++)
                await publisher.DispatchBatchAsync(deadline.Token);
            var repository = new ExecutionRepository(db);
            await foreach (var message in results.ConsumeAsync<string>(cancellationToken: deadline.Token))
            {
                await message.AckAsync(cancellationToken: deadline.Token);
                if (message.Data is null || !message.Data.Contains(execution.Id.ToString(), StringComparison.Ordinal)) continue;
                if (message.Subject == Subjects.ExecutionFailedV2)
                {
                    if (!disabledConnection && !structuredRejected && foreSitePayload is null or "current") throw new InvalidOperationException("Hosted package execution failed: " + message.Data);
                    var failed = JsonSerializer.Deserialize<MessageEnvelope<ExecutionFailedV2>>(message.Data, Options)!;
                    var expected = structuredRejected ? "EXECUTION_INPUT_QUALITY_REJECTED" : disabledConnection ? "EXECUTION_CONNECTION_DISABLED" : foreSitePayload switch
                    {
                        "missing" => "FORESITE_MEASUREMENT_MISSING",
                        "invalid-time" => "FORESITE_TIMESTAMP_INVALID",
                        "future" => "EXECUTION_INPUT_FUTURE",
                        _ => "EXECUTION_INPUT_STALE"
                    };
                    Assert(failed.Payload.FailureCode == expected && failed.Payload.InputDiagnostics?.Count == 1,
                        "ForeSite acquisition did not produce bounded typed input failure evidence.");
                    var diagnostic = failed.Payload.InputDiagnostics!.Single();
                    Assert(diagnostic.ConnectionId == connectionId && diagnostic.AssetId == asset &&
                        diagnostic.OutcomeCode == expected &&
                        diagnostic.Quality == (foreSitePayload is "scan" or "future" ? "Good" : "Unknown"),
                        "Failure evidence lost the authorized source or rejection code.");
                    if (foreSitePayload is "scan" or "future")
                        Assert(diagnostic.Value == 99.6m && diagnostic.Unit == "%" &&
                            diagnostic.TimestampKind == "ControllerScan" && diagnostic.RetrievedAtUtc is not null &&
                            diagnostic.TimestampUtc == fixture.AcquisitionTime,
                            "Controller acquisition evidence lost its value, offset normalization or retrieval provenance.");
                    else Assert(diagnostic.Value is null, "A missing/refused read invented an input value.");
                    await repository.ApplyFailedWithDiagnosticsAsync(failed.MessageId, "hosted-check", execution.Id,
                        failed.Payload.StartedAtUtc, failed.Payload.FailedAtUtc, failed.Payload.FailureCode,
                        JsonSerializer.Serialize(failed.Payload.InputDiagnostics, Options));
                    break;
                }
                if (message.Subject == Subjects.ExecutionStartedV2)
                {
                    var started = JsonSerializer.Deserialize<MessageEnvelope<ExecutionStartedV2>>(message.Data, Options)!;
                    await repository.ApplyStartedAsync(started.MessageId, "hosted-check", execution.Id, started.Payload.StartedAtUtc);
                    Assert((await repository.GetAsync(execution.Id))?.Status == ExecutionStatus.Running, "V3 never entered Running.");
                    sawStarted = true;
                }
                else
                {
                    var completed = JsonSerializer.Deserialize<MessageEnvelope<ExecutionCompletedV2>>(message.Data, Options)!;
                    Assert(completed.Payload.ResultCode == "HOSTED_PROBE_COMPLETED" && completed.Payload.OutputJson.Contains(structured ? "valueContractVersion" : "99.6", StringComparison.Ordinal),
                        "Hosted result lost the normalized input or algorithm output.");
                    await repository.ApplyCompletedAsync(completed.MessageId, "hosted-check", execution.Id,
                        completed.Payload.StartedAtUtc, completed.Payload.CompletedAtUtc, completed.Payload.ResultCode, completed.Payload.OutputJson);
                    if (structured)
                    {
                        var persisted = (await repository.GetAsync(execution.Id))!.OutputJson!;
                        using var output = JsonDocument.Parse(persisted);
                        var inputData = output.RootElement.GetProperty("inputObservations")[0].GetProperty("value").GetProperty("data");
                        var curve = output.RootElement.GetProperty("engineeringResults")[0].GetProperty("value").GetProperty("data");
                        Assert(inputData.GetArrayLength() == 2 && curve.GetArrayLength() == 2 &&
                            curve[0].GetProperty("load").GetArrayLength() == 400 && curve[1].GetProperty("load").GetArrayLength() == 112 &&
                            curve[1].GetProperty("load")[111].GetDecimal() == 111m * 12345.67890123456789m * 2,
                            "Structured read or curve output was truncated or lowered into a scalar.");
                        foreach (var index in new[] { 0, 1 })
                            foreach (var property in new[] { "position", "id", "kind", "capturedAtUtc", "positionUnit", "loadUnit" })
                                Assert(inputData[index].GetProperty(property).GetRawText() == curve[index].GetProperty(property).GetRawText(),
                                    "Structured execution lost card metadata or sample pairing.");
                    }
                    sawCompleted = true;
                    break;
                }
            }
            Assert(sawStarted && (disabledConnection || structuredRejected || foreSitePayload is not null and not "current"
                ? (await repository.GetAsync(execution.Id)) is { Status: ExecutionStatus.Failed, OutputJson: not null }
                : sawCompleted && (await repository.GetAsync(execution.Id))?.Status == ExecutionStatus.Completed),
                "Real approved archive did not complete through V3 dispatch and V2 results.");
            if (structuredRejected)
            {
                var stored = (await repository.GetAsync(execution.Id))!.OutputJson!;
                using var failure = JsonDocument.Parse(stored);
                Assert(failure.RootElement.GetProperty("inputDiagnostics")[0].GetProperty("value").ValueKind == JsonValueKind.Null &&
                    !stored.Contains("engineeringResults", StringComparison.Ordinal) && !stored.Contains("capturedAtUtc", StringComparison.Ordinal),
                    "Rejected card input persisted module results or unbounded card payloads.");
            }
            if (foreSitePayload is not null)
            {
                Assert(fixture.TokenReads == 1 && fixture.MeasurementReads == 1,
                    "Authorized acquisition did not use the shared ForeSite session/parser exactly once.");
                var stored = (await repository.GetAsync(execution.Id))!;
                using var evidence = JsonDocument.Parse(stored.OutputJson!);
                var observations = evidence.RootElement.GetProperty(foreSitePayload == "current" ? "inputObservations" : "inputDiagnostics");
                Assert(observations.GetArrayLength() == 1 &&
                    !stored.OutputJson!.Contains("private-fixture", StringComparison.Ordinal) &&
                    !stored.OutputJson.Contains("fs=", StringComparison.Ordinal) &&
                    (foreSitePayload == "current" || !stored.OutputJson.Contains("controlIntents", StringComparison.Ordinal)),
                    "Persisted result leaked settings/cookies or admitted failed module outputs.");
            }
            if (foreSitePayload == "current")
            {
                var stored = (await repository.GetAsync(execution.Id))!;
                using var output = JsonDocument.Parse(stored.OutputJson!);
                var input = output.RootElement.GetProperty("inputObservations")[0];
                Assert(input.GetProperty("quality").GetString() == "Good" &&
                    input.GetProperty("source").GetString() == "foresite:controller-scan" &&
                    input.GetProperty("timestampUtc").GetDateTimeOffset() == fixture.AcquisitionTime,
                    "Successful ForeSite execution lost the scan acquisition evidence.");
            }
            Assert(!Directory.EnumerateFileSystemEntries(working).Any(), "Successful execution retained working files.");
            Console.WriteLine("Hosted package E2E: upload, approval, pinned dispatch, NATS, worker loading, input read, result persistence and retirement boundary passed.");
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(stop.Token);
            if (savedConnection) await reads.StopAsync(stop.Token);
            Environment.SetEnvironmentVariable("WSA_HOSTED_EXECUTION_TEST_ID", null);
            if (Directory.Exists(working)) Directory.Delete(working, recursive: true);
        }

        async Task RefuseDispatchAsync(string code)
        {
            await using var attempt = new WellSiteAutoPilotDbContext(options);
            try { await new ExecutionService(new ExecutionRepository(attempt), TimeProvider.System).RequestConfiguredShadowAsync(
                command with { ConfiguredLogicId = Guid.NewGuid(), ScheduledForUtc = now.AddMinutes(1) }, moduleId); }
            catch (WellSiteAutoPilotException error) when (error.Code == code) { return; }
            throw new InvalidOperationException("Unapproved/retired package accepted a new execution.");
        }
    }

    private static JsonElement CardSchema() => JsonSerializer.Deserialize<JsonElement>("""
        {"type":"array","minItems":1,"maxItems":16,"items":{"type":"object","properties":{
          "id":{"type":"string"},"kind":{"type":"string"},"capturedAtUtc":{"type":"string"},
          "positionUnit":{"type":"string"},"loadUnit":{"type":"string"},
          "position":{"type":"array","items":{"type":"number"},"minItems":2,"maxItems":1024},
          "load":{"type":"array","items":{"type":"number"},"minItems":2,"maxItems":1024}},
          "required":["id","kind","capturedAtUtc","positionUnit","loadUnit","position","load"],"additionalProperties":false}}
        """);

    private static byte[] Pack(LogicModuleManifest manifest)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open())) writer.Write(JsonSerializer.Serialize(manifest, Options));
            using var image = File.OpenRead(Assembly.GetExecutingAssembly().Location);
            using var output = archive.CreateEntry("payload/probe.dll").Open();
            image.CopyTo(output);
        }
        return bytes.ToArray();
    }
    // Retain synthetic execution-contract cases alongside the real ForeSite parser cases.
    private sealed class RecordedMeasurements(Guid connectionId, DateTimeOffset timestamp, ForeSiteConnectionMeasurements? measurements, bool structured, string structuredQuality) : IExecutionMeasurementGateway
    {
        public Task<EngineeringValue> GetCurrentAsync(AuthorizedExecutionRead read, CancellationToken cancellationToken = default)
        {
            Assert(read.ConnectionId == connectionId && read.ConnectionRevision == 1 && read.ExternalId == "42" && read.Quantity == (structured ? "SurfaceCards" : "PumpFillage"),
                "Hosted read escaped its authorized saved source.");
            if (structured)
                return Task.FromResult(new EngineeringValue("SurfaceCards", null, "", timestamp, structuredQuality, "synthetic-structured-fixture")
                { StructuredValue = new("foresite.surface-cards/v1", JsonSerializer.SerializeToElement(CardPointCounts.Select((count, i) => new { id = "card-" + i, kind = i == 0 ? "CURRENT" : "FULL", capturedAtUtc = timestamp.AddSeconds(-i), positionUnit = "in", loadUnit = "lb", position = Enumerable.Range(0, count).ToArray(), load = Enumerable.Range(0, count).Select(n => n * 12345.67890123456789m).ToArray() }))) });
            return measurements is null
                ? Task.FromResult(new EngineeringValue("PumpFillage", 99.6m, "%", timestamp, "Good", "verified-test-fixture"))
                : measurements.ReadAsync(new ConnectionMeasurementRequest(read.ConnectionId, read.ConnectionRevision,
                    read.Settings.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal),
                    read.ExternalId, read.Quantity), cancellationToken);
        }
    }
    // Real Gateway session and parser with only the external HTTP transport replaced.
    private sealed class ForeSiteHttpFixture(string? payload, DateTimeOffset now) : IHttpClientFactory
    {
        private string? Payload { get; } = payload;
        public DateTimeOffset AcquisitionTime { get; } = payload switch
        {
            "scan" => now.AddMinutes(-5),
            "future" => now.AddMinutes(5),
            _ => now
        };
        public DateTimeOffset LastAttemptTime { get; } = now;
        public int TokenReads { get; private set; }
        public int MeasurementReads { get; private set; }
        public HttpClient CreateClient(string name) => new(new Handler(this));
        private sealed class Handler(ForeSiteHttpFixture fixture) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert(request.Method == HttpMethod.Get && request.RequestUri!.Host == "private-fixture",
                    "Shadow wrote to ForeSite or escaped the saved connection.");
                if (request.RequestUri!.AbsolutePath == "/prefix/TokenService/CreateToken")
                {
                    fixture.TokenReads++;
                    var token = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                    token.Headers.TryAddWithoutValidation("Set-Cookie", "fs=private; Path=/prefix");
                    return Task.FromResult(token);
                }
                Assert(request.RequestUri.AbsolutePath == "/prefix/Surveillance/well/42" &&
                    request.Headers.TryGetValues("Cookie", out var cookies) && cookies.Single() == "fs=private",
                    "ForeSite acquisition lost the virtual directory, saved well or token cookie.");
                fixture.MeasurementReads++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    fixture.Payload == "missing" ? "{\"Value\":{}}" :
                    JsonSerializer.Serialize(new { Value = new {
                        CurrentPumpFillage = 99.6m,
                        LastScanTime = fixture.LastAttemptTime,
                        LastGoodScanTime = fixture.Payload == "invalid-time" ? null :
                            fixture.AcquisitionTime.ToOffset(TimeSpan.FromHours(-5)).ToString("O"),
                        CommStatus = fixture.Payload == "scan" ? "Failed" : "Succeeded"
                    } })) });
            }
        }
    }
    private sealed class ProbeData(DateTimeOffset timestamp) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert(request.Method == HttpMethod.Get, "Shadow issued a provider write.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            { quantity = "PumpFillage", value = 99.6m, unit = "%", timestampUtc = timestamp, quality = "Good", source = "simulator" }, Options)) });
        }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

public sealed class HostedExecutionProbe : ILogicModuleV1
{
    public LogicModuleIdentity Identity { get; } = new(Environment.GetEnvironmentVariable("WSA_HOSTED_EXECUTION_TEST_ID")!, "1.0.0");
    public ValueTask<LogicModuleExecutionResult> ExecuteAsync(LogicModuleExecutionContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new LogicModuleExecutionResult("HOSTED_PROBE_COMPLETED",
            [new("observed", context.Inputs.Single().AssetId, "PumpFillage", context.Inputs.Single().Value, "%")], [], [], new Dictionary<string, string>()));
}


public sealed class HostedStructuredProbe : ILogicModuleV2
{
    public LogicModuleIdentity Identity { get; } = new(Environment.GetEnvironmentVariable("WSA_HOSTED_EXECUTION_TEST_ID")!, "1.0.0");
    public ValueTask<LogicModuleExecutionResultV2> ExecuteAsync(LogicModuleExecutionContextV2 context, CancellationToken cancellationToken = default)
    {
        var cards = System.Text.Json.Nodes.JsonNode.Parse(context.Inputs.Single().Value.Data.GetRawText())!.AsArray();
        foreach (var card in cards)
        {
            var load = card!["load"]!.AsArray();
            for (var i = 0; i < load.Count; i++) load[i] = load[i]!.GetValue<decimal>() * 2;
        }
        return ValueTask.FromResult(new LogicModuleExecutionResultV2("HOSTED_PROBE_COMPLETED",
            [new("curve", context.Inputs.Single().AssetId, "Curve", new("fixture.curve/v1", JsonSerializer.SerializeToElement(cards)), null)],
            [], [], new Dictionary<string, string>()));
    }
}
