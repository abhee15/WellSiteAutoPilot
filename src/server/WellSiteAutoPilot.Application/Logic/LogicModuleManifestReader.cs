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
        Stream source, CancellationToken cancellationToken = default) =>
        (await ReadDocumentAsync(source, cancellationToken)).Manifest;

    internal static async Task<(LogicModuleManifest Manifest, string NormalizedJson)> ReadDocumentAsync(
        Stream source, CancellationToken cancellationToken = default)
    {
        using var document = await JsonDocument.ParseAsync(source, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Module manifest must be an object.");
        ValidateUniqueFields(document.RootElement);
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
        await LogicModuleManifestValidator.ValidateSchemasAsync(manifest, cancellationToken);
        return (manifest, JsonSerializer.Serialize(document.RootElement));
    }
    // DTO names are case-insensitive; schema property names are case-sensitive data.
    internal static void ValidateUniqueFields(JsonElement element, bool inSchema = false)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) ValidateUniqueFields(item, inSchema);
        }
        if (element.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(inSchema ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new JsonException("Module manifest contains a duplicate field.");
            ValidateUniqueFields(property.Value, inSchema || property.Name.Equals("schema", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("parameterSchema", StringComparison.OrdinalIgnoreCase));
        }
    }

}
