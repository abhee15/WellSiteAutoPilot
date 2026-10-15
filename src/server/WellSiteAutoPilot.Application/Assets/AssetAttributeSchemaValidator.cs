using System.Text;
using System.Text.Json;
using NJsonSchema;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Assets;

/// <summary>
/// Explicit offline-safe, fail-closed subset of JSON Schema. Reject unsupported
/// keywords, including all references, before using the MIT NJsonSchema parser.
/// </summary>
public static class AssetAttributeSchemaValidator
{
    public const int MaximumSchemaBytes = 32768;
    public const int MaximumAttributesBytes = 65536;
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "type", "properties", "required", "additionalProperties", "items",
        "minLength", "maxLength", "minimum", "maximum", "minItems", "maxItems",
        "description", "title", "enum"
    };
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "object", "array", "string", "integer", "number", "boolean", "null"
    };

    public static async Task ValidateDefinitionAsync(
        string schemaJson, CancellationToken cancellationToken = default)
    {
        using var doc = Parse(schemaJson, MaximumSchemaBytes,
            "ASSET_ATTRIBUTE_SCHEMA_INVALID",
            "The Asset Type attribute schema is malformed or too large.");
        ValidateShape(doc.RootElement, 0, isRoot: true);
        try
        {
            _ = await JsonSchema.FromJsonAsync(schemaJson, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new WellSiteAutoPilotException(
                "ASSET_ATTRIBUTE_SCHEMA_INVALID", FailureKind.Validation,
                "The Asset Type attribute schema could not be parsed.",
                innerException: ex);
        }
    }

    public static async Task ValidateValuesAsync(
        string schemaJson, string attributesJson,
        CancellationToken cancellationToken = default)
    {
        using var values = Parse(attributesJson, MaximumAttributesBytes,
            "ASSET_ATTRIBUTE_VALUES_INVALID", "Asset attributes must be valid JSON.");
        if (values.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw InvalidValues();
        }
        // Duplicate JSON property names are ambiguous across parsers and can
        // defeat operator review. Reject them recursively before evaluation.
        RejectDuplicateValues(values.RootElement);
        // Existing database schema rows are not automatically trusted either.
        await ValidateDefinitionAsync(schemaJson, cancellationToken);
        try
        {
            var schema = await JsonSchema.FromJsonAsync(schemaJson, cancellationToken);
            if (schema.Validate(attributesJson).Count != 0)
            {
                throw InvalidValues();
            }
        }
        catch (WellSiteAutoPilotException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new WellSiteAutoPilotException(
                "ASSET_ATTRIBUTE_SCHEMA_INVALID", FailureKind.Validation,
                "Asset Type schema evaluation failed.", innerException: ex);
        }
    }

    private static WellSiteAutoPilotException InvalidValues() => new(
        "ASSET_ATTRIBUTE_VALUES_INVALID", FailureKind.Validation,
        "Asset attributes do not match the selected Asset Type schema.");

    private static JsonDocument Parse(string? text, int limit, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            Encoding.UTF8.GetByteCount(text) > limit)
        {
            throw new WellSiteAutoPilotException(code, FailureKind.Validation, message);
        }
        try
        {
            return JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException ex)
        {
            throw new WellSiteAutoPilotException(
                code, FailureKind.Validation, message, innerException: ex);
        }
    }

    private static void RejectDuplicateValues(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                RejectDuplicateValues(item);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw InvalidValues();
            RejectDuplicateValues(property.Value);
        }
    }

    private static void ValidateShape(JsonElement node, int depth, bool isRoot)
    {
        if (depth > 6 || node.ValueKind != JsonValueKind.Object)
        {
            throw Unsupported();
        }
        string? type = null;
        var seenKeywords = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keyword in node.EnumerateObject())
        {
            if (!seenKeywords.Add(keyword.Name))
                throw Unsupported();
            if (!Keywords.Contains(keyword.Name))
            {
                // $ref, $id, $schema, dynamic references, regex patterns, etc:
                // never silently ignore or attempt a network/file lookup.
                throw Unsupported();
            }
            switch (keyword.Name)
            {
                case "type":
                    if (keyword.Value.ValueKind != JsonValueKind.String ||
                        !Types.Contains(keyword.Value.GetString() ?? ""))
                        throw Unsupported();
                    type = keyword.Value.GetString();
                    break;
                case "properties":
                    if (keyword.Value.ValueKind != JsonValueKind.Object ||
                        keyword.Value.EnumerateObject().Count() > 64)
                        throw Unsupported();
                    foreach (var property in keyword.Value.EnumerateObject())
                    {
                        if (string.IsNullOrWhiteSpace(property.Name) ||
                            property.Name.Length > 128)
                            throw Unsupported();
                        ValidateShape(property.Value, depth + 1, false);
                    }
                    break;
                case "items":
                    ValidateShape(keyword.Value, depth + 1, false);
                    break;
                case "required":
                    if (keyword.Value.ValueKind != JsonValueKind.Array ||
                        keyword.Value.GetArrayLength() > 64 ||
                        keyword.Value.EnumerateArray().Any(
                            item => item.ValueKind != JsonValueKind.String ||
                                    string.IsNullOrWhiteSpace(item.GetString())))
                        throw Unsupported();
                    break;
                case "additionalProperties":
                    if (keyword.Value.ValueKind is not
                        (JsonValueKind.True or JsonValueKind.False))
                        throw Unsupported();
                    break;
                case "minLength":
                case "maxLength":
                case "minItems":
                case "maxItems":
                    if (keyword.Value.ValueKind != JsonValueKind.Number ||
                        !keyword.Value.TryGetInt32(out var count) ||
                        count is < 0 or > 1024)
                        throw Unsupported();
                    break;
                case "minimum":
                case "maximum":
                    if (keyword.Value.ValueKind != JsonValueKind.Number)
                        throw Unsupported();
                    break;
                case "description":
                case "title":
                    if (keyword.Value.ValueKind != JsonValueKind.String ||
                        keyword.Value.GetString()!.Length > 512)
                        throw Unsupported();
                    break;
                case "enum":
                    if (keyword.Value.ValueKind != JsonValueKind.Array ||
                        keyword.Value.GetArrayLength() is < 1 or > 64)
                        throw Unsupported();
                    break;
            }
        }
        if (isRoot && type is not (null or "object"))
            throw Unsupported();
        if (node.TryGetProperty("properties", out _) && type is not (null or "object"))
            throw Unsupported();
        if (node.TryGetProperty("items", out _) && type != "array")
            throw Unsupported();
    }

    private static WellSiteAutoPilotException Unsupported() => new(
        "ASSET_ATTRIBUTE_SCHEMA_UNSUPPORTED", FailureKind.Validation,
        "The Asset Type uses a constraint outside the supported offline schema profile.");
}
