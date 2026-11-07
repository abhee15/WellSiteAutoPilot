using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.IntegrationGateway.Adapters;

internal static class AdapterOperationChecks
{
    public static void Run()
    {
        foreach (var adapter in new AdapterCatalog().List())
        {
            var operations = AdapterOperations.List(adapter);
            Check(operations.Select(o => o.Key).Distinct(StringComparer.Ordinal).Count() == operations.Count, "Duplicate operation keys.");
            foreach (var operation in operations)
            {
                Check(operation.Capability is ProviderCapabilityKind.CurrentData or ProviderCapabilityKind.HistoricalData, "Unsafe public operation.");
                Check(Version.TryParse(operation.ContractVersion, out var version) && version.Major == 1, "Invalid contract version.");
                Check(operation.Request.Fields.Count > 0 && operation.Response.Fields.Count > 0, "Missing schema fields.");
                Check(operation.Limits.MaximumQuantities == 1 && operation.Limits.MaximumReplyBytes > 0, "Missing read bounds.");
                if (operation.Capability == ProviderCapabilityKind.CurrentData)
                    Check(AdapterReadRules.Find(adapter.Key)?.AllowsCurrentRead(operation.Quantity,
                        operation.StructuredSchemaId is null ? null : operation.CapabilityId, operation.StructuredSchemaId) == true,
                        "Descriptor disagrees with executable current read rules.");
            }
        }
        var broad = new AdapterDescriptor("test", "Test", "Test", 1, [], [
            new(ProviderCapabilityKind.CurrentData, "Current"), new(ProviderCapabilityKind.AssetDiscovery, "Discovery"),
            new(ProviderCapabilityKind.Command, "Control"), new(ProviderCapabilityKind.Calculation, "Calculation")]);
        Check(AdapterOperations.List(broad).Count == 0, "Broad capabilities must not invent operations.");
        var declared = EngineeringOperationSchemas.Current("test", "Test", "Test", "q", "kind", null, "Source", "Source", 1);
        Check(AdapterOperations.List(broad with { Operations = [declared with { Capability = ProviderCapabilityKind.Command }] }).Count == 0,
            "Command metadata leaked into public operations.");
        Check(AdapterOperations.List(broad with { Capabilities = [], Operations = [declared] }).Count == 0,
            "Operation without capability must fail closed.");
    }
    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
