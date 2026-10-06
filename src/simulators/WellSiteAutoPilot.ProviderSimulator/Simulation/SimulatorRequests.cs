namespace WellSiteAutoPilot.ProviderSimulator.Simulation;

public sealed record SetCurrentValueRequest(
    decimal Value,
    string Unit,
    string Quality = "Good");

public sealed record SetAvailabilityRequest(bool Available);

public sealed record ResetScenarioRequest(string Scenario);
