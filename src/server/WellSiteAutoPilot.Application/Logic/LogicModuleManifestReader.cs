using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Domain.Logic;

namespace WellSiteAutoPilot.Application.Logic;

public static class LogicModuleManifestReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    // Shared by standalone CLI manifests and manifests inside inspected archives.
    public static async Task<LogicModuleManifest> ReadAsync(
        Stream source, CancellationToken cancellationToken = default)
    {
        using var document = await JsonDocument.ParseAsync(source, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Module manifest must be an object.");
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!fields.Add(property.Name)) throw new JsonException("Module manifest contains a duplicate field.");
        }
        string[] required = ["manifestVersion", "moduleId", "version", "displayName", "publisher", "runtime",
            "executionProfile", "entryPoint", "minimumSdkVersion", "assetRequirements", "dataRequirements", "commandRequirements"];
        if (required.Any(field => !fields.Contains(field))) throw new JsonException("Module manifest is missing a required field.");
        // Nullable requirement fields such as canonicalUnit and requiredAssetTypeKey
        // retain their existing optional semantics when omitted.
        var manifest = document.RootElement.Deserialize<LogicModuleManifest>(SerializerOptions)
            ?? throw new JsonException("Module manifest is empty.");
        LogicModuleManifestValidator.Validate(manifest);
        await LogicModuleManifestValidator.ValidateParameterSchemaAsync(manifest, cancellationToken);
        return manifest;
    }
}
