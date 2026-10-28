using System.Text.Json;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>
/// Offline authoring validation using the existing bounded, reference-free schema profile.
/// A wrapper allows primitive, array and record roots without changing Asset schemas.
/// </summary>
public static class StructuredValueValidation
{
    private static readonly string[] RequiredValue = ["value"];
    public static async Task ValidateAsync(StructuredValue value, string expectedSchemaId,
        JsonElement schema, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(expectedSchemaId) || expectedSchemaId.Length > 128 ||
            value.SchemaId != expectedSchemaId || schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("type", out _) || value.Data.ValueKind == JsonValueKind.Undefined)
            throw InvalidValue();

        try
        {
            // The same byte/depth/duplicate-key/reference limits apply to both directions.
            var wrappedSchema = JsonSerializer.Serialize(new
            {
                type = "object", properties = new Dictionary<string, JsonElement> { ["value"] = schema },
                required = RequiredValue, additionalProperties = false
            });
            var wrappedValue = JsonSerializer.Serialize(new { value = value.Data });
            await AssetAttributeSchemaValidator.ValidateValuesAsync(wrappedSchema, wrappedValue, cancellationToken);
            ValidateFiniteNumbers(value.Data);
        }
        catch (WellSiteAutoPilotException error) when (error.Code.StartsWith("ASSET_ATTRIBUTE_", StringComparison.Ordinal))
        {
            throw new WellSiteAutoPilotException("MODULE_VALUE_INVALID", FailureKind.Validation,
                "The structured value or its schema is invalid or exceeds the supported limits.", innerException: error);
        }
    }

    private static void ValidateFiniteNumbers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number &&
            (!value.TryGetDouble(out var number) || !double.IsFinite(number))) throw InvalidValue();
        if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateFiniteNumbers(item);
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var item in value.EnumerateObject()) ValidateFiniteNumbers(item.Value);
    }

    private static WellSiteAutoPilotException InvalidValue() => new("MODULE_VALUE_INVALID", FailureKind.Validation,
        "The structured value does not match its declared schema identity or shape.");
}
