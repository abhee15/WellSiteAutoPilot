using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

internal static class StructuredManifestChecks
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };
    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    public static async Task RunAsync()
    {
        var shape = new LogicValueSchema("example.matrix/v1", Json("""
            {"type":"array","items":{"type":"array","items":{"type":"number"},"minItems":2,"maxItems":2}}
            """));
        var manifest = new LogicModuleManifest(2, "example.structured", "1.0.0", "Structured", "Test",
            LogicRuntimeKind.DotNet, ExecutionProfile.ScheduledOneShot, "Example.Module", "1.2.0",
            [new("well", 1, 1, null, [])],
            [new("matrix", "well", "Curve", "Current", null, 60, false, shape, "structured.current")], [],
            OutputRequirements: [new("curve", "Curve", null, shape)]);
        await LogicModuleManifestValidator.ValidateSchemasAsync(manifest);
        var serialized = JsonSerializer.Serialize(manifest, Options);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(serialized));
        var reread = await LogicModuleManifestReader.ReadAsync(stream);
        if (reread.ManifestVersion != 2 || reread.DataRequirements.Single().RequiredCapability != "structured.current" ||
            !JsonElement.DeepEquals(reread.OutputRequirements!.Single().ValueSchema.Schema, shape.Schema))
            throw new InvalidOperationException("Reviewed structured contract did not round-trip.");
        using var archiveBytes = new MemoryStream();
        using (var archive = new ZipArchive(archiveBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open())) await writer.WriteAsync(serialized);
            using (var writer = new StreamWriter(archive.CreateEntry("payload/README.txt").Open())) await writer.WriteAsync("inspection-only fixture");
        }
        archiveBytes.Position = 0;
        var inspected = await LogicModulePackageReader.ReadAsync(archiveBytes);
        if (inspected.Manifest.DataRequirements.Single().ValueSchema!.SchemaId != shape.SchemaId)
            throw new InvalidOperationException("Package inspection lost the schema contract.");
        LogicModulePackageCompatibility.ValidateCatalog(inspected, manifest.ModuleId, manifest.Version,
            inspected.Sha256, inspected.ManifestJson);
        using var offlineStream = new MemoryStream(Encoding.UTF8.GetBytes(serialized));
        var definition = await StructuredModuleOfflineHost.ReadDefinitionAsync(offlineStream);
        if (definition.Inputs.Single().SchemaId != shape.SchemaId || definition.Outputs.Single().SchemaId != shape.SchemaId ||
            definition.AssetRequirements!.Single().MaximumCount != 1)
            throw new InvalidOperationException("Offline host lost reviewed requirements.");
        foreach (var (invalid, code) in new[]
        {
            (manifest with { ManifestVersion = 1 }, "MODULE_CONTRACT_INVALID"),
            (manifest with { ManifestVersion = 3 }, "MODULE_MANIFEST_VERSION_UNSUPPORTED"),
            (manifest with { MinimumSdkVersion = "1.1.0" }, "MODULE_CONTRACT_INVALID"),
            (manifest with { MinimumSdkVersion = "1.2.0-rc.1" }, "MODULE_CONTRACT_INVALID"),
            (manifest with { Runtime = LogicRuntimeKind.Python }, "MODULE_CONTRACT_INVALID"),
            (manifest with { OutputRequirements = null }, "MODULE_CONTRACT_INVALID"),
            (manifest with { OutputRequirements = [] }, "MODULE_CONTRACT_INVALID"),
            (manifest with { OutputRequirements = [null!] }, "MODULE_CONTRACT_INVALID"),
            (manifest with { OutputRequirements = [new("curve", "Curve", null, null!)] }, "MODULE_CONTRACT_INVALID"),
            (manifest with { OutputRequirements = [new("curve", "Curve", null, shape), new("CURVE", "Curve", null, shape)] }, "MODULE_OUTPUT_DUPLICATE"),
            (manifest with { DataRequirements = [manifest.DataRequirements.Single() with { ValueSchema = null }] }, "MODULE_CONTRACT_INVALID"),
            (manifest with { DataRequirements = [manifest.DataRequirements.Single() with { RequiredCapability = null }] }, "MODULE_CONTRACT_INVALID"),
            (manifest with { DataRequirements = [manifest.DataRequirements.Single() with { RequiredCapability = "not a capability" }] }, "MODULE_IDENTIFIER_INVALID"),
            (manifest with { OutputRequirements = [new("curve", "Curve", null, shape with { Schema = Json("""{"type":"string"}""") })] }, "MODULE_VALUE_SCHEMA_INVALID"),
            (manifest with { OutputRequirements = [new("curve", "Curve", null, new("other/v1", Json("""{"type":"number","$ref":"https://invalid.example"}""")))] }, "MODULE_VALUE_SCHEMA_INVALID"),
            (manifest with { OutputRequirements = [new("curve", "Curve", null, new("other/v1", Json("{}")))] }, "MODULE_VALUE_SCHEMA_INVALID")
        }) await Reject(() => LogicModuleManifestValidator.ValidateSchemasAsync(invalid), code);
        // Schema object property names remain case-sensitive data, unlike DTO fields.
        await LogicModuleManifestValidator.ValidateSchemasAsync(manifest with
        { OutputRequirements = [new("record", "Record", null, new("record/v1", Json("""{"type":"object","properties":{"A":{"type":"number"},"a":{"type":"string"}}}""")))] });
        foreach (var text in new[]
        {
            serialized.Replace("\"requiredCapability\":\"structured.current\"", "\"requiredCapability\":\"structured.current\",\"RequiredCapability\":\"other\"", StringComparison.Ordinal),
            serialized.Replace("\"schemaId\":\"example.matrix/v1\"", "\"schemaId\":\"example.matrix/v1\",\"schemaId\":\"other\"", StringComparison.Ordinal)
        })
        {
            using var duplicate = new MemoryStream(Encoding.UTF8.GetBytes(text));
            try { await LogicModuleManifestReader.ReadAsync(duplicate); }
            catch (JsonException) { continue; }
            throw new InvalidOperationException("Duplicate nested contract field was accepted.");
        }
        await Reject(() => { LogicModulePackageCompatibility.ValidateHost(manifest, LogicRuntimeKind.DotNet, "1.2.0"); return Task.CompletedTask; }, "MODULE_CONTRACT_UNSUPPORTED");
        var id = Guid.NewGuid();
        var revision = new ConfiguredLogicRevision(Guid.NewGuid(), id, 1, manifest.ModuleId, manifest.Version, serialized,
            ExecutionMode.Shadow, "{}", ConfiguredLogicRevisionStatus.Active, DateTimeOffset.UtcNow, null, null, null, [], []);
        var configuration = new ConfiguredLogicDefinition(id, "Structured", revision.Id, DateTimeOffset.UtcNow, [revision]);
        await Reject(() => { ConfiguredExecutionSnapshot.Create(configuration, revision, ExecutionTriggerKind.Manual, null); return Task.CompletedTask; }, "MODULE_CONTRACT_UNSUPPORTED");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try { await LogicModuleManifestValidator.ValidateSchemasAsync(manifest, cancellation.Token); }
        catch (OperationCanceledException) { Console.WriteLine("Structured manifest, snapshot and capability checks passed."); return; }
        throw new InvalidOperationException("Schema validation ignored cancellation.");
    }

    private static async Task Reject(Func<Task> action, string code)
    {
        try { await action(); }
        catch (WellSiteAutoPilotException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected " + code);
    }
}
