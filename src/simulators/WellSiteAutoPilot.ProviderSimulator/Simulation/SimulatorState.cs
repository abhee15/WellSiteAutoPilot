using System.Collections.Concurrent;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.ProviderSimulator.Simulation;

public sealed class SimulatorState
{
    private readonly ConcurrentDictionary<(string AssetId, string Quantity), EngineeringValue> _values = new();

    public SimulatorState()
    {
        Reset("rod-pump-normal");
    }

    public string Scenario { get; private set; } = "rod-pump-normal";

    public bool ProviderAvailable { get; private set; } = true;

    public EngineeringValue GetCurrent(string assetId, string quantity)
    {
        if (!ProviderAvailable)
        {
            throw new SimulatorProviderUnavailableException();
        }

        if (_values.TryGetValue((assetId, quantity), out var configured))
        {
            return configured with
            {
                TimestampUtc = DateTimeOffset.UtcNow
            };
        }

        return new EngineeringValue(
            quantity,
            Value: 0m,
            Unit: "simulated",
            TimestampUtc: DateTimeOffset.UtcNow,
            Quality: "Good",
            Source: $"simulator:{assetId}");
    }

    public void SetCurrent(
        string assetId,
        string quantity,
        decimal value,
        string unit,
        string quality = "Good")
    {
        _values[(assetId, quantity)] = new EngineeringValue(
            quantity,
            value,
            unit,
            DateTimeOffset.UtcNow,
            quality,
            $"simulator:{assetId}");
    }

    public void SetAvailability(bool available)
    {
        ProviderAvailable = available;
    }

    public void Reset(string scenario)
    {
        _values.Clear();
        ProviderAvailable = true;
        Scenario = scenario;

        if (string.Equals(scenario, "rod-pump-normal", StringComparison.OrdinalIgnoreCase))
        {
            SetCurrent("WELL-101", "PumpFillage", 82m, "%");
            SetCurrent("WELL-101", "CurrentSPM", 5.5m, "spm");
            SetCurrent("WELL-101", "TargetPumpFillage", 80m, "%");
        }
    }
}

public sealed class SimulatorProviderUnavailableException : Exception
{
    public SimulatorProviderUnavailableException()
        : base("The simulated provider is unavailable.")
    {
    }
}
