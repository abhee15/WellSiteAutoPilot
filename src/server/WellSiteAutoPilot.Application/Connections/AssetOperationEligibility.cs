using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Connections;

public enum AssetOperationEligibilityState
{
    SourceCheckRequired = 1, ConnectionDisabled, ConnectionIncompatible, ConnectionTestRequired,
    ConnectionUnavailable, Unsupported, AssetInactive, Unmapped, AmbiguousSource, ResourceUnsupported
}

public sealed record AssetOperationEligibility(string Key, string ContractVersion, AssetOperationEligibilityState State, string Reason);
public sealed record AssetOperationsSnapshot(Guid ConnectionId, int ConnectionRevision, Guid AssetId, string AdapterKey,
    string ConnectionState, IReadOnlyList<AssetOperationEligibility> Operations);

/// <summary>Metadata eligibility only: a matching declaration and binding never prove live point availability.</summary>
public static class AssetOperationEligibilityEvaluator
{
    public static IReadOnlyList<AssetOperationEligibility> Evaluate(ConnectionRecord connection, AdapterDescriptor? adapter, AssetConnectionSource source)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(source);
        if (adapter is null || adapter.Key != connection.AdapterKey || source.ConnectionId != connection.Id) return [];
        var readiness = ConnectionOperationAvailability.Evaluate(connection, adapter);
        return adapter.ReadOperations().Select(operation =>
        {
            var (state, reason) = Assess(operation);
            return new AssetOperationEligibility(operation.Key, operation.ContractVersion, state, reason);
        }).ToArray();

        (AssetOperationEligibilityState, string) Assess(AdapterOperationDescriptor operation)
        {
            if (readiness.State == "Disabled") return (AssetOperationEligibilityState.ConnectionDisabled, "The saved connection is disabled.");
            if (readiness.State == "Incompatible") return (AssetOperationEligibilityState.ConnectionIncompatible, "Saved settings do not match the installed adapter version.");
            if (!source.IsActive) return (AssetOperationEligibilityState.AssetInactive, "The selected Asset is inactive.");
            if (source.Bindings.Count == 0) return (AssetOperationEligibilityState.Unmapped, "This Asset has no imported source binding in the selected Connection.");
            if (source.Bindings.Count != 1) return (AssetOperationEligibilityState.AmbiguousSource, "Multiple source bindings require review; no source was chosen automatically.");
            var binding = source.Bindings[0];
            if (string.IsNullOrWhiteSpace(binding.ExternalId)) return (AssetOperationEligibilityState.Unmapped, "The imported source identity is missing.");
            if (operation.ResourceKind is null || operation.ResourceKind != binding.ResourceKind)
                return (AssetOperationEligibilityState.ResourceUnsupported, "This operation does not declare support for the imported resource kind.");
            if (operation.Capability == ProviderCapabilityKind.CurrentData &&
                AdapterReadRules.Find(adapter.Key)?.AllowsCurrentRead(operation.Quantity,
                    operation.StructuredSchemaId is null ? null : operation.CapabilityId, operation.StructuredSchemaId) != true)
                return (AssetOperationEligibilityState.Unsupported, "The operation does not match this adapter's executable read contracts.");
            if (readiness.State == "Unknown") return (AssetOperationEligibilityState.ConnectionTestRequired, "Test the current Connection revision before checking this source.");
            if (readiness.State != "ConnectionReady") return (AssetOperationEligibilityState.ConnectionUnavailable, "The latest connection test did not establish readiness.");
            return (AssetOperationEligibilityState.SourceCheckRequired,
                "The declaration and imported source match. Point mapping, device/quantity availability, quality and values have not been verified. Execution requires separate fresh authorization and bounded reads.");
        }
    }
}
