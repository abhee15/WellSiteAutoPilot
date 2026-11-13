using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Worker.DotNet.Execution;

namespace WellSiteAutoPilot.ExecutionContractV2Tests;

internal static class DeclarativePackageChecks
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private const string Schema = """
        {"type":"object","properties":{"threshold":{"type":"number","minimum":0,"maximum":100}},"additionalProperties":false}
        """;
    private const string Definition = """
        {"parameter":"threshold","default":70,"outcomeCode":"LOW_FILLAGE","title":"Low pump fillage","detail":"Review pump fillage."}
        """;

    public static async Task RunAsync(ExecutionRequestedV2 template, DateTimeOffset now)
    {
        var asset = template.Assets.First() with { Role = "well", ParameterOverridesJson = "{}" };
        var binding = template.Inputs.First() with { AssetId = asset.AssetId };
        var manifest = new LogicModuleManifest(1, "guided.fillage", "1.0.0", "Guided alert", "Test",
            LogicRuntimeKind.DotNet, ExecutionProfile.ScheduledOneShot, DeclarativeThresholdModule.EntryPoint, "1.1.0",
            [new("well", 1, 1, null, [])], [new(binding.RequirementId, "well", binding.Quantity, binding.Access,
                binding.CanonicalUnit, binding.MaximumAgeSeconds, binding.AllowUncertainQuality)], [],
            ParameterSchema: JsonSerializer.Deserialize<JsonElement>(Schema));
        var bytes = Pack(manifest, Definition);
        using var package = new MemoryStream(bytes);
        var inspected = await LogicModulePackageReader.ReadAsync(package);
        var preview = inspected.DeclarativeModule ?? throw new InvalidOperationException("No declarative evaluator.");
        var request = template with { ModuleId = manifest.ModuleId, ModuleVersion = manifest.Version,
            ParametersJson = "{}", Assets = [asset], Inputs = [binding] };
        var hosted = new ExecutionRequestedV3(request, new(inspected.Sha256, inspected.ManifestJson));
        var directory = Path.Combine(Path.GetTempPath(), "wsa-guided-" + Guid.NewGuid().ToString("N"));
        var packageHost = new DotNetModulePackageHost(new CopySource(bytes), directory);

        foreach (var (value, settings, overrides, expected) in new[]
        {
            (62m, "{}", "{}", "LOW_FILLAGE"),
            (70m, "{}", "{}", "THRESHOLD_NOT_MET"),
            (62m, """{"threshold":60}""", "{}", "THRESHOLD_NOT_MET"),
            (62m, """{"threshold":60}""", """{"threshold":65}""", "LOW_FILLAGE")
        })
        {
            var context = new LogicModuleExecutionContext(request.ExecutionId, preview.Identity, "guided", now, settings,
                [new("well", asset.AssetId, overrides)],
                [new(binding.RequirementId, asset.AssetId, binding.Quantity, value, "%", now, "Good", "fixture")]);
            var expectedResult = await preview.ExecuteAsync(context);
            using var client = new HttpClient(new Measurement(now, value)) { BaseAddress = new("http://fixture.invalid") };
            var engine = new ModuleExecutionEngine(new GatewayDataClient(client), new BuiltInLogicModuleResolver(),
                packageHost: packageHost);
            var observed = await engine.ExecuteObservedAsync(hosted with
            {
                Execution = request with { ParametersJson = settings, Assets = [asset with { ParameterOverridesJson = overrides }] }
            }, "guided", now);
            if (expectedResult.OutcomeCode != expected || observed.Result.OutcomeCode != expected ||
                !observed.Result.Recommendations.SequenceEqual(expectedResult.Recommendations) ||
                observed.Result.ControlIntents.Count != 0 || observed.InputObservations.Single().Value != value)
                throw new InvalidOperationException("Guided preview and hosted execution did not agree.");

            await RejectAsync(() => engine.ExecuteObservedAsync(hosted with
            {
                Execution = request with { Inputs = [binding with { MaximumAgeSeconds = 3600 }] }
            }, "guided", now), "EXECUTION_INPUT_CONTRACT_MISMATCH");
        }

        using (var missing = new HttpClient(new Measurement(now, null)) { BaseAddress = new("http://fixture.invalid") })
        {
            var engine = new ModuleExecutionEngine(new GatewayDataClient(missing), new BuiltInLogicModuleResolver(),
                packageHost: packageHost);
            await RejectAsync(() => engine.ExecuteObservedAsync(hosted, "guided", now), "EXECUTION_INPUT_SHAPE_UNSUPPORTED");
        }
        using (var client = new HttpClient(new Measurement(now, 62m)) { BaseAddress = new("http://fixture.invalid") })
        {
            var engine = new ModuleExecutionEngine(new GatewayDataClient(client), new BuiltInLogicModuleResolver(),
                packageHost: packageHost);
            await RejectAsync(() => engine.ExecuteObservedAsync(hosted with
            {
                Execution = request with { ParametersJson = """{"threshold":101}""" }
            }, "guided", now), "CONFIGURED_LOGIC_PARAMETERS_INVALID");
        }

        await RejectAsync(() => InspectAsync(manifest, "{\"unknown\":true}"), "MODULE_PACKAGE_INVALID");
        await RejectAsync(() => InspectAsync(manifest with { EntryPoint = "wsa.declarative.threshold.v2" }, Definition),
            "MODULE_DECLARATIVE_VERSION_UNSUPPORTED");
        await RejectAsync(() => InspectAsync(manifest with { DataRequirements = [manifest.DataRequirements.Single() with { Access = "History" }] }, Definition),
            "MODULE_DECLARATIVE_CONTRACT_INVALID");
        await RejectAsync(() => InspectAsync(manifest, Definition.Replace("70", "101", StringComparison.Ordinal)),
            "CONFIGURED_LOGIC_PARAMETERS_INVALID");
        await RejectAsync(() => InspectAsync(manifest, Definition, extraPayload: true), "MODULE_PACKAGE_INVALID");
        if (Directory.Exists(directory)) throw new InvalidOperationException("Declarative execution extracted package files.");
        Console.WriteLine("Guided package and worker parity passed for defaults, overrides, equality, missing inputs, and invalid artifacts.");
    }

    private static async Task InspectAsync(LogicModuleManifest manifest, string definition, bool extraPayload = false)
    {
        using var bytes = new MemoryStream(Pack(manifest, definition, extraPayload));
        await LogicModulePackageReader.ReadAsync(bytes);
    }

    private static byte[] Pack(LogicModuleManifest manifest, string definition, bool extraPayload = false)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                writer.Write(JsonSerializer.Serialize(manifest, Options));
            using (var writer = new StreamWriter(archive.CreateEntry(DeclarativeThresholdModule.PayloadPath).Open()))
                writer.Write(definition);
            if (extraPayload) archive.CreateEntry("payload/unexpected.dll");
        }
        return bytes.ToArray();
    }

    private static async Task RejectAsync(Func<Task> operation, string code)
    {
        try { await operation(); }
        catch (WellSiteAutoPilotException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected rejection: " + code);
    }

    private sealed class CopySource(byte[] bytes) : IModulePackageSource
    {
        public Task CopyToAsync(string sha256, Stream destination, CancellationToken cancellationToken) =>
            destination.WriteAsync(bytes, cancellationToken).AsTask();
    }

    private sealed class Measurement(DateTimeOffset now, decimal? value) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("Guided execution issued a write.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { quantity = "PumpFillage", value, unit = "%", timestampUtc = now, quality = "Good", source = "fixture" })
            });
        }
    }
}
