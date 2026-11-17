using System.Text.Json;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>
/// Decides whether a declared draft input can be checked against a connection, and if not, says why. Authors need the
/// reason: an unregistered quantity, an unsupported access mode and a wrong value type are fixed in different places.
/// </summary>
public static class GuidedInputSupport
{
    public const string Generic = "GUIDED_LIVE_INPUT_UNSUPPORTED";
    public const string AccessUnsupported = "GUIDED_LIVE_ACCESS_UNSUPPORTED";
    public const string AdapterMismatch = "GUIDED_LIVE_ADAPTER_MISMATCH";
    public const string CapabilityUnsupported = "GUIDED_LIVE_CAPABILITY_UNSUPPORTED";
    public const string ValueTypeUnsupported = "GUIDED_LIVE_VALUE_TYPE_UNSUPPORTED";
    public const string QuantityUnregistered = "GUIDED_LIVE_QUANTITY_UNREGISTERED";

    /// <summary>Null when the input can be checked on this connection; otherwise the reason code.</summary>
    public static string? UnsupportedReason(string? requiredAdapterKey, string adapterKey, string resourceKind, LogicDataRequirement input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Access != "Current") return AccessUnsupported;
        if (requiredAdapterKey is not null && !string.Equals(requiredAdapterKey, adapterKey, StringComparison.OrdinalIgnoreCase))
            return AdapterMismatch;
        if (AdapterReadRules.Find(adapterKey) is not { } rule || resourceKind != rule.ResourceKind) return Generic;
        if (input.ValueSchema is null)
            return rule.AllowsCurrentRead(input.Quantity, input.RequiredCapability, null) ? null : QuantityUnregistered;
        if (rule.AllowsScalarCurrentDeclaration(input.Quantity, input.RequiredCapability, RootType(input.ValueSchema))) return null;
        // A structured declaration is supported when the provider registers that quantity with this exact capability and schema id.
        if (input.RequiredCapability == AdapterReadRules.StructuredCurrentCapability)
            return rule.AllowsCurrentRead(input.Quantity, input.RequiredCapability, input.ValueSchema.SchemaId) ? null : QuantityUnregistered;
        if (input.RequiredCapability != AdapterReadRules.EngineeringCurrentCapability) return CapabilityUnsupported;
        if (RootType(input.ValueSchema) != "number") return ValueTypeUnsupported;
        return QuantityUnregistered;
    }

    private static string? RootType(LogicValueSchema schema) =>
        schema.Schema.ValueKind == JsonValueKind.Object &&
        schema.Schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString() : null;
}
