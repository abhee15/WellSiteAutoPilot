using System.Text.Json;
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
            var groups = adapter.ReadCapabilityGroups();
            Check(groups.SelectMany(group => group.OperationKeys).Order().SequenceEqual(operations.Select(operation => operation.Key).Order()), "Capability catalog lost or duplicated an operation");
            foreach (var operation in operations)
            {
                Check(operation.Capability is ProviderCapabilityKind.CurrentData or ProviderCapabilityKind.HistoricalData or ProviderCapabilityKind.Alarms or ProviderCapabilityKind.Calculation or ProviderCapabilityKind.ConfigurationData, "Unsafe public operation.");
                Check(Version.TryParse(operation.ContractVersion, out var version) && version.Major == 1, "Invalid contract version.");
                Check(operation.Request.Fields.Count > 0 && operation.Response.Fields.Count > 0, "Missing schema fields.");
                Check(operation.Limits.MaximumQuantities == 1 && operation.Limits.MaximumReplyBytes > 0, "Missing read bounds.");
                var docs = operation.Documentation ?? throw new InvalidOperationException("Missing typed API documentation");
                Check(!string.IsNullOrWhiteSpace(docs.Invocation) && docs.ExecutionBoundary.Contains("Integration Gateway", StringComparison.Ordinal), "Missing invocation/boundary");
                using var input = JsonDocument.Parse(docs.RequestExampleJson);
                using var output = JsonDocument.Parse(docs.ResponseExampleJson);
                foreach (var field in operation.Request.Fields.Where(field => field.Required && !field.Name.Contains('.')))
                    Check(input.RootElement.TryGetProperty(field.Name, out _), "Request example is missing a required field");
                foreach (var field in operation.Response.Fields.Where(field => field.Required && !field.Name.Contains('.')))
                    Check(output.RootElement.TryGetProperty(field.Name, out _), "Response example is missing a required field");
                var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                if (operation.Capability == ProviderCapabilityKind.ConfigurationData)
                    Check(JsonSerializer.Deserialize<AssetConfigurationResult>(docs.ResponseExampleJson, json)?.AssetExternalId == "101", "Configuration example type mismatch");
                if (operation.Capability == ProviderCapabilityKind.HistoricalData)
                {
                    var history = JsonSerializer.Deserialize<HistoricalReadResult>(docs.ResponseExampleJson, json)!;
                    Check(history.Quantity == operation.Quantity && history.Samples.All(MeasurementPayloadLimits.IsValid), "History example type mismatch");
                    HistoricalReadLimits.Validate(JsonSerializer.Deserialize<HistoricalReadRequest>(docs.RequestExampleJson, json)!);
                }
                if (operation.Capability == ProviderCapabilityKind.CurrentData)
                {
                    var value = JsonSerializer.Deserialize<EngineeringValue>(docs.ResponseExampleJson, json)!;
                    Check(value.Quantity == operation.Quantity && MeasurementPayloadLimits.IsValid(value), "Current/structured example type mismatch");
                }
                if (operation.Capability == ProviderCapabilityKind.CurrentData)
                    Check(AdapterReadRules.Find(adapter.Key)?.AllowsCurrentRead(operation.Quantity,
                        operation.StructuredSchemaId is null ? null : operation.CapabilityId, operation.StructuredSchemaId) == true,
                        "Descriptor disagrees with executable current read rules.");
            }
        }
        var catalogAdapter = new AdapterCatalog().Find("foresite")!;
        var validGroup = catalogAdapter.OperationGroups![0];
        foreach (var group in new[] { validGroup with { ContractVersion = "2.0" }, validGroup with { OperationKeys = ["missing"] },
            validGroup with { OperationKeys = [validGroup.OperationKeys[0], validGroup.OperationKeys[0]] } })
            Check((catalogAdapter with { OperationGroups = [group] }).ReadCapabilityGroups().Count == 0, "Invalid group was advertised");
        Check((catalogAdapter with { OperationGroups = [validGroup, validGroup] }).ReadCapabilityGroups().Count == 0, "Duplicate group keys were advertised");
        var broad = new AdapterDescriptor("test", "Test", "Test", 1, [], [
            new(ProviderCapabilityKind.CurrentData, "Current"), new(ProviderCapabilityKind.AssetDiscovery, "Discovery"),
            new(ProviderCapabilityKind.Command, "Control"), new(ProviderCapabilityKind.Calculation, "Calculation")]);
        Check(AdapterOperations.List(broad).Count == 0, "Broad capabilities must not invent operations.");
        var declared = EngineeringOperationSchemas.Current("test", "Test", "Test", "q", "kind", null, "Source", "Source", 1);
        Check(AdapterOperations.List(broad with { Operations = [declared with { Capability = ProviderCapabilityKind.Command }] }).Count == 0,
            "Command metadata leaked into public operations.");
        Check(AdapterOperations.List(broad with { Operations = [declared with { Capability = ProviderCapabilityKind.Calculation }] }).Count == 1,
            "An explicitly declared typed calculation must not require provider-specific application changes.");
        Check(AdapterOperations.List(broad with { Operations = [declared with { ContractVersion = "2.0" }] }).Count == 0,
            "Unknown major operation contracts must not be published as supported.");
        Check(AdapterOperations.List(broad with { Capabilities = [], Operations = [declared] }).Count == 0,
            "Operation without capability must fail closed.");
    }
    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
