using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.IntegrationGateway.Adapters;

/// <summary>Every adapter this build ships is held to the command-definition rules, so a new one cannot advertise commands loosely.</summary>
internal static class ShippedAdapterCommandChecks
{
    public static void Run()
    {
        var adapters = new AdapterCatalog().List();
        if (adapters.Count == 0) throw new InvalidOperationException("The adapter catalogue is empty.");
        foreach (var adapter in adapters)
        {
            var problems = AdapterCommandDefinitionValidator.Validate(adapter);
            if (problems.Count > 0)
                throw new InvalidOperationException($"Adapter '{adapter.Key}' has invalid command definitions: {string.Join(" ", problems)}");
            if (adapter.MinimumScheduleSeconds < 0)
                throw new InvalidOperationException($"Adapter '{adapter.Key}' has a negative minimum schedule.");
        }
    }
}
