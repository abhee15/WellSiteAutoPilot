using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.IntegrationGateway.Adapters;

/// <summary>
/// Read-only operation metadata for an adapter's Operations explorer.
/// Discovery is intentionally excluded: it remains available to connection import workflows.
/// This projection does not grant permission to execute any operation.
/// </summary>
public static class AdapterOperations
{
    public static IReadOnlyList<AdapterOperationSummary> List(AdapterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return descriptor.Capabilities
            .Where(capability => capability.Kind is
                ProviderCapabilityKind.CurrentData or
                ProviderCapabilityKind.HistoricalData or
                ProviderCapabilityKind.Alarms or
                ProviderCapabilityKind.Calculation)
            .GroupBy(capability => capability.Kind)
            .Select(group =>
            {
                var capability = group.First();
                return new AdapterOperationSummary(
                    group.Key.ToString(),
                    capability.Name,
                    capability.Kind,
                    capability.ContractVersion,
                    AdapterOperationEffect.ReadOnly);
            })
            .ToArray();
    }
}

public enum AdapterOperationEffect
{
    ReadOnly = 0
}

public sealed record AdapterOperationSummary(
    string Key,
    string DisplayName,
    ProviderCapabilityKind Capability,
    string ContractVersion,
    AdapterOperationEffect Effect);
