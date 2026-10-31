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

    private readonly ConcurrentDictionary<string, SimulatedCommandResult> _commands = new();

    // Commands only change simulator state; there is no real device. Idempotent per key.
    public SimulatedCommandResult ApplyCommand(SimulatedCommandRequest request)
    {
        if (!ProviderAvailable)
        {
            throw new SimulatorProviderUnavailableException();
        }

        return _commands.GetOrAdd(request.IdempotencyKey, key =>
        {
            if (!string.IsNullOrWhiteSpace(request.Quantity))
            {
                SetCurrent(request.AssetId, request.Quantity, request.Value, request.Unit ?? "simulated");
            }

            return new SimulatedCommandResult($"sim-cmd:{Guid.NewGuid():N}", "Applied", request.AssetId,
                request.Command, request.Value, DateTimeOffset.UtcNow);
        });
    }

    public SimulatedCommandResult ReadbackCommand(string idempotencyKey)
    {
        if (!ProviderAvailable)
        {
            throw new SimulatorProviderUnavailableException();
        }

        return _commands.TryGetValue(idempotencyKey, out var applied)
            ? applied
            : new SimulatedCommandResult(string.Empty, "NotApplied", string.Empty, string.Empty, null, DateTimeOffset.MinValue);
    }

    public IReadOnlyCollection<SimulatedCommandResult> Commands => _commands.Values.ToArray();

    public void Reset(string scenario)
    {
        _values.Clear();
        _commands.Clear();
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
