using System.Collections.Concurrent;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.ProviderSimulator.Simulation;

public sealed class SimulatorState
{
    private readonly ConcurrentDictionary<(string AssetId, string Quantity), EngineeringValue> _values = new();
    private readonly object _pendingLock = new();
    private readonly List<(DateTimeOffset DueUtc, string AssetId, string Quantity, decimal Value, string Unit)> _pending = [];
    private volatile HashSet<string> _blockedReadback = [];

    public SimulatorState()
    {
        Reset("rod-pump-normal");
    }

    public string Scenario { get; private set; } = "rod-pump-normal";

    public bool ProviderAvailable { get; private set; } = true;

    public EngineeringValue GetCurrent(string assetId, string quantity)
    {
        if (!ProviderAvailable || _blockedReadback.Contains(quantity))
        {
            throw new SimulatorProviderUnavailableException();
        }

        ApplyDue();

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

    /// <summary>Queues a change to a current value that takes effect at <paramref name="dueUtc"/>, as a command does.</summary>
    public void ScheduleCurrent(string assetId, string quantity, decimal value, string unit, DateTimeOffset dueUtc)
    {
        lock (_pendingLock) _pending.Add((dueUtc, assetId, quantity, value, unit));
        ApplyDue();
    }

    /// <summary>Quantities whose current value cannot be read, to simulate a provider that cannot confirm a command.</summary>
    public void SetReadbackBlocked(IEnumerable<string> quantities) => _blockedReadback = quantities.ToHashSet(StringComparer.Ordinal);

    private void ApplyDue()
    {
        var now = DateTimeOffset.UtcNow;
        List<(DateTimeOffset DueUtc, string AssetId, string Quantity, decimal Value, string Unit)> due;
        lock (_pendingLock)
        {
            due = _pending.Where(item => item.DueUtc <= now).OrderBy(item => item.DueUtc).ToList();
            _pending.RemoveAll(item => item.DueUtc <= now);
        }

        foreach (var item in due) SetCurrent(item.AssetId, item.Quantity, item.Value, item.Unit);
    }

    public void SetAvailability(bool available)
    {
        ProviderAvailable = available;
    }

    public void Reset(string scenario)
    {
        _values.Clear();
        lock (_pendingLock) _pending.Clear();
        _blockedReadback = [];
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
