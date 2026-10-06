using WellSiteAutoPilot.Integration.Contracts.Providers;

var cygnet = new ProviderDescriptor(
    "cygnet-primary",
    "CygNet",
    "SCADA",
    "1.0.0",
    "existing-provider-version",
    [
        new ProviderCapability(ProviderCapabilityKind.CurrentData, "Current engineering data"),
        new ProviderCapability(ProviderCapabilityKind.HistoricalData, "Historical engineering data"),
        new ProviderCapability(ProviderCapabilityKind.Alarms, "Alarms"),
        new ProviderCapability(ProviderCapabilityKind.AssetDiscovery, "Asset discovery"),
        new ProviderCapability(ProviderCapabilityKind.Command, "Governed commands"),
        new ProviderCapability(ProviderCapabilityKind.Verification, "Command verification")
    ]);

var wami = new ProviderDescriptor(
    "wami-primary",
    "WAMI",
    "EngineeringCalculation",
    "1.0.0",
    "existing-provider-version",
    [
        new ProviderCapability(ProviderCapabilityKind.Calculation, "Engineering calculations")
    ]);

if (!cygnet.Capabilities.Any(item => item.Kind == ProviderCapabilityKind.Command) ||
    cygnet.Capabilities.Any(item => item.Kind == ProviderCapabilityKind.Calculation))
{
    throw new InvalidOperationException("CygNet semantic capability example is invalid.");
}

if (wami.Capabilities.Count != 1 ||
    wami.Capabilities.Single().Kind != ProviderCapabilityKind.Calculation)
{
    throw new InvalidOperationException("WAMI semantic capability example is invalid.");
}

var compatible = new ProviderCompatibility(
    cygnet.ProviderId,
    ProviderCompatibilityState.Compatible,
    cygnet.AdapterVersion,
    cygnet.ProviderVersion);

if (compatible.State != ProviderCompatibilityState.Compatible)
{
    throw new InvalidOperationException("Provider compatibility contract failed.");
}

Console.WriteLine("Provider-neutral adapter contract checks passed.");
return 0;
