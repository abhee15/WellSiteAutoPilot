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
        var definition = new ThresholdDefinition("threshold", 70, "LOW_FILLAGE", "Low pump fillage", "Review pump fillage.");
        var artifact = await DeclarativeThresholdPackageBuilder.BuildAsync(manifest, definition);
        var rebuilt = await DeclarativeThresholdPackageBuilder.BuildAsync(manifest, definition);
        var changed = await DeclarativeThresholdPackageBuilder.BuildAsync(manifest, definition with { Default = 71 });
        if (!artifact.Bytes.Span.SequenceEqual(rebuilt.Bytes.Span) || artifact.Package.Sha256 == changed.Package.Sha256)
            throw new InvalidOperationException("The artifact is unstable or a reviewed definition edit did not change its hash.");
        var bytes = artifact.Bytes.ToArray();
        var inspected = artifact.Package;
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
            var sample = new DeclarativeThresholdSample(request.ExecutionId, "guided", asset.AssetId, now, settings, overrides,
                new(binding.RequirementId, asset.AssetId, binding.Quantity, value, "%", now, "Good", "fixture"));
            var evidence = await DeclarativeThresholdPreview.EvaluateAsync(artifact, sample);
            var expectedResult = evidence.Result ?? throw new InvalidOperationException(evidence.FailureCode);
            if (evidence.PackageSha256 != inspected.Sha256 || evidence.Sample != sample)
                throw new InvalidOperationException("Preview evidence did not preserve its artifact and effective sample.");
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

        var unavailable = await DeclarativeThresholdPreview.EvaluateAsync(artifact,
            new(request.ExecutionId, "guided", asset.AssetId, now, "{}", "{}", null));
        if (unavailable.Result is not null || unavailable.FailureCode != "MODULE_DECLARATIVE_INPUT_UNAVAILABLE" ||
            unavailable.PackageSha256 != inspected.Sha256)
            throw new InvalidOperationException("Unavailable input was not retained as failed preview evidence.");

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
        var draft = new GuidedModuleDraft(Guid.NewGuid(), Guid.NewGuid(), 1, new(manifest, definition));
        var edited = draft.Edit(1, draft.Content with { Definition = definition with { Default = 71 } });
        var reviewSample = new DeclarativeThresholdSample(request.ExecutionId, "guided", asset.AssetId, now, "{}", "{}", null);
        await RejectAsync(() => edited.PrepareSubmissionAsync(1, [reviewSample]), "GUIDED_DRAFT_REVISION_CONFLICT");
        var submission = await edited.PrepareSubmissionAsync(2, [reviewSample]);
        if (submission.DraftId != draft.Id || submission.DraftRevision != 2 ||
            submission.Artifact.Package.Sha256 != changed.Package.Sha256 ||
            submission.Evidence.Single().PackageSha256 != submission.Artifact.Package.Sha256)
            throw new InvalidOperationException("Submission evidence was not pinned to the selected draft revision.");

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
