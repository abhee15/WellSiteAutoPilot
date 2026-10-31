namespace WellSiteAutoPilot.ProviderSimulator.Simulation;

public sealed record SetCurrentValueRequest(
    decimal Value,
    string Unit,
    string Quality = "Good");

public sealed record SetAvailabilityRequest(bool Available);

public sealed record ResetScenarioRequest(string Scenario);

public sealed record SimulatedCommandRequest(
    string IdempotencyKey,
    string AssetId,
    string Command,
    string? Quantity,
    decimal Value,
    string? Unit);

public sealed record SimulatedCommandResult(
    string Reference,
    string State,
    string AssetId,
    string Command,
    decimal? ObservedValue,
    DateTimeOffset AppliedAtUtc);
