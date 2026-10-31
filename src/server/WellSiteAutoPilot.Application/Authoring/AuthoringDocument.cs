using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Authoring;

/// <summary>
/// Authoring project document (candidate contract): named inputs, parameters, typed calculation steps,
/// conditions, outputs and proposed actions. It is not a manifest and grants no execution permission.
/// Decimal literals are canonical decimal text; every edge names an explicit operand.
/// </summary>
public sealed record AuthoringDocument(
    string Schema,
    AuthoringModuleIdentity Module,
    string Profile,
    IReadOnlyList<AuthoringAssetRole> AssetRoles,
    IReadOnlyList<AuthoringInput> Inputs,
    IReadOnlyList<AuthoringParameter> Parameters,
    IReadOnlyList<AuthoringStep> Steps,
    IReadOnlyList<AuthoringCondition> Conditions,
    IReadOnlyList<AuthoringOutput> Outputs,
    IReadOnlyList<AuthoringAction> Actions)
{
    public const string SchemaId = "wsa-authoring-draft/1";
    public const string DecimalProfile = "decimal-checked/1";
}

public sealed record AuthoringModuleIdentity(
    string ModuleId, string Version, string DisplayName, string Publisher,
    string? Description = null, string? RequiredAdapterKey = null);

public sealed record AuthoringAssetRole(string Role, string? RequiredAssetTypeKey = null);

public sealed record AuthoringValueSchema(string SchemaId, JsonElement Schema);

public sealed record AuthoringInput(
    string Id, string Label, string Quantity, string? Unit, string AssetRole,
    int? MaximumAgeSeconds = null, bool AllowUncertainQuality = false,
    AuthoringValueSchema? ValueSchema = null, string? RequiredCapability = null);

public sealed record AuthoringParameter(string Id, string Label, string DefaultValue, string? Unit = null);

/// <summary>An edge endpoint. Kind is input, parameter, step or literal; Port selects a code-step output.</summary>
public sealed record AuthoringOperand(string Kind, string? Ref = null, string? Port = null, string? Value = null);

public sealed record AuthoringCodeStep(
    string Language, string EntryPoint, string Source,
    IReadOnlyList<string> InputPorts, IReadOnlyList<string> OutputPorts);

/// <summary>Kind is calculation (Operation + Operands) or code (an opaque, source-preserving module step).</summary>
public sealed record AuthoringStep(
    string Id, string Label, string Kind, string? Operation = null,
    IReadOnlyList<AuthoringOperand>? Operands = null, AuthoringCodeStep? Code = null);

public sealed record AuthoringCondition(
    string Id, string Label, AuthoringOperand Left, string Comparator, AuthoringOperand Right);

public sealed record AuthoringOutput(
    string Id, string Label, string Quantity, string Unit, AuthoringOperand Source,
    AuthoringValueSchema? ValueSchema = null);

public sealed record AuthoringAction(
    string Id, string Label, string Condition, string Title, string Detail,
    string? Quantity = null, string? Unit = null, AuthoringOperand? SuggestedValue = null, string? Command = null);

public static class AuthoringJson
{
    public const int MaximumDocumentBytes = 512 * 1024;

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false
    };

    /// <summary>Strictly reads a document; unknown members or malformed shapes are rejected.</summary>
    public static AuthoringDocument Read(JsonElement element)
    {
        var text = element.GetRawText();
        if (Encoding.UTF8.GetByteCount(text) > MaximumDocumentBytes)
            throw Invalid("The authoring document exceeds 512 KiB.");
        try
        {
            var document = JsonSerializer.Deserialize<AuthoringDocument>(text, Options);
            return document is null ? throw Invalid("The authoring document is empty.") : document;
        }
        catch (JsonException error)
        {
            throw Invalid("The authoring document shape is invalid: " + error.Message);
        }
    }

    public static JsonElement ToElement(AuthoringDocument document) =>
        JsonSerializer.SerializeToElement(document, Options);

    /// <summary>The revision identity of a document: SHA-256 of its canonical serialization.</summary>
    public static string Digest(AuthoringDocument document) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(document, Options)));

    private static WellSiteAutoPilotException Invalid(string detail) =>
        new("AUTHORING_DOCUMENT_INVALID", FailureKind.Validation, detail);
}
