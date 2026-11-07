using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.IntegrationGateway.Adapters;

/// <summary>Only explicit implemented reads are public. Discovery and control never appear in this projection.</summary>
public static class AdapterOperations
{
    public static IReadOnlyList<AdapterOperationDescriptor> List(AdapterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.ReadOperations();
    }
}
