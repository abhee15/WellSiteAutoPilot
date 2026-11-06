using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>Which timestamps the simulator serves: fresh ones, or years-old ones like the idle CYG_DEMO domain.</summary>
public enum SimulatedCygNetProfile { Current, DemoStale }

/// <summary>
/// A deterministic stand-in for a CygNet facility service, for development, demonstration and tests. It serves the same
/// facilities and points for any domain, site and service, never touches the network, and stamps values relative to the
/// supplied clock so freshness rules behave. Values are fixed per well so module outcomes are repeatable:
/// WELL_001 healthy, WELL_002 low fillage, WELL_003 high casing-tubing differential, WELL_004 high motor current,
/// WELL_005 uncertain and stale, WELL_006 has points that never received a value.
/// With <see cref="SimulatedCygNetProfile.DemoStale"/> every value is years old, like an idle demo domain.
/// </summary>
public sealed class SimulatedCygNetClient(TimeProvider? clock = null, SimulatedCygNetProfile profile = SimulatedCygNetProfile.Current) : ICygNetClient
{
    public const string Source = "cygnet-simulated";
    private const string NoValueWell = "WELL_006";

    private sealed record Point(decimal Value, string Unit, string Quality = "Good", int AgeSeconds = 5);

    private static readonly CygNetFacility[] Facilities =
    [
        new("BATTERY_A", "Pad A battery", "BATTERY", "Pad A"),
        new("WELL_001", "Pad A well 1 (healthy)", "WELL", "Pad A"),
        new("WELL_002", "Pad A well 2 (pumped off)", "WELL", "Pad A"),
        new("WELL_003", "Pad A well 3 (high casing pressure)", "WELL", "Pad A"),
        new("WELL_004", "Pad B well 4 (high motor current)", "WELL", "Pad B"),
        new("WELL_005", "Pad B well 5 (stale, uncertain data)", "WELL", "Pad B"),
        new("WELL_006", "Pad B well 6 (no values yet)", "WELL", "Pad B"),
        new("TANK_01", "Pad B tank", "TANK", "Pad B")
    ];

    private static Dictionary<string, Point> Well(decimal fillage, decimal casing, decimal tubing, decimal amps, decimal rod,
        string fillageQuality = "Good", int age = 5) => new(StringComparer.Ordinal)
    {
        ["PumpFillage"] = new(fillage, "%", fillageQuality, age),
        ["CasingPressure"] = new(casing, "psi", AgeSeconds: age),
        ["TubingPressure"] = new(tubing, "psi", AgeSeconds: age),
        ["MotorAmps"] = new(amps, "A", AgeSeconds: age),
        ["RodLoad"] = new(rod, "lbf", AgeSeconds: age)
    };

    private static readonly Dictionary<string, Dictionary<string, Point>> Points = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WELL_001"] = Well(82.5m, 210m, 145m, 38.2m, 21500m),
        ["WELL_002"] = Well(34.0m, 190m, 150m, 41.0m, 23800m),
        ["WELL_003"] = Well(71.0m, 395m, 120m, 40.5m, 22400m),
        ["WELL_004"] = Well(65.0m, 205m, 140m, 71.5m, 26100m),
        ["WELL_005"] = Well(58.0m, 200m, 142m, 39.0m, 21900m, "Uncertain", 600)
    };

    // Same age as the CYG_DEMO points that were last written in October 2020.
    private const int DemoStaleSeconds = 6 * 365 * 86400;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<IReadOnlyList<string>> CheckAsync(CygNetConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        // Wells only (batteries and tanks have no rod-lift points), and the unfinished well only when asked for by the filter.
        var wells = Facilities.Select(f => f.Id).Where(id => id.StartsWith("WELL_", StringComparison.Ordinal) &&
            (connection.FacilityFilter is { } filter ? CygNetAdapter.Matches(filter, id) : id != NoValueWell)).ToArray();
        return Task.FromResult<IReadOnlyList<string>>(CygNetFreshness.Pick(wells));
    }

    public Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CygNetFacilityList(Facilities));
    }

    public Task<IReadOnlyList<CygNetPoint>> ListPointsAsync(CygNetConnection connection, string facilityId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Points.ContainsKey(facilityId) && !string.Equals(facilityId, NoValueWell, StringComparison.OrdinalIgnoreCase))
            throw new CygNetReadException("CYGNET_FACILITY_NOT_FOUND", "CygNet has no such facility.");
        return Task.FromResult<IReadOnlyList<CygNetPoint>>(
        [
            new("RRLPFILAVG", "Average Pump Fillage", "%"),
            new("PRCASXIN", "Casing Pressure", "psi"),
            new("PRTUBXIN", "Tubing Pressure", "psi"),
            new("RRLMXLDDLY", "Maximum Load Daily", "lbf"),
            new("RRLSTKYEST", "Strokes Yesterday", ""),
            new("TMRUNYD", "Run Time Yesterday", "second")
        ]);
    }

    public Task<CygNetHistory> ReadHistoryAsync(
        CygNetConnection connection, string facilityId, string udc, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Points.ContainsKey(facilityId))
            throw new CygNetReadException("CYGNET_FACILITY_NOT_FOUND", "CygNet has no history for this facility.");
        if (!CygNetCatalog.IsUdc(udc))
            throw new CygNetReadException("CYGNET_POINT_NOT_FOUND", "CygNet has no such point.");
        // One entry every 15 minutes on the quarter hour; the value steps with the hour so averages are predictable.
        const long Step = 15 * 60;
        var entries = new List<CygNetHistoryEntry>();
        for (var t = (startUtc.ToUnixTimeSeconds() + Step - 1) / Step * Step; t <= endUtc.ToUnixTimeSeconds(); t += Step)
            entries.Add(new(DateTimeOffset.FromUnixTimeSeconds(t), 80 + t / 3600 % 5, Unreliable: false));
        return Task.FromResult(new CygNetHistory(entries));
    }

    public Task<IReadOnlyList<CygNetAlarm>> ReadAlarmsAsync(CygNetConnection connection, string facilityId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Points.ContainsKey(facilityId))
            throw new CygNetReadException("CYGNET_FACILITY_NOT_FOUND", "CygNet has no alarms for this facility.");
        var now = _clock.GetUtcNow();
        // WELL_003 has its high casing pressure alarm set; every other well is quiet apart from one cleared alarm.
        IReadOnlyList<CygNetAlarm> alarms = string.Equals(facilityId, "WELL_003", StringComparison.OrdinalIgnoreCase)
            ? [new("PRCASXIN", true, 2, now.AddMinutes(-42), "395"), new("RRLPFILAVG", false, 3, now.AddHours(-9), "28")]
            : [new("RRLPFILAVG", false, 3, now.AddHours(-9), "28")];
        return Task.FromResult(alarms);
    }

    public Task<EngineeringValue> ReadCurrentAsync(
        CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AdapterReadRules.CygNetQuantities.Contains(quantity))
            throw new CygNetReadException("CYGNET_QUANTITY_UNSUPPORTED", "This quantity is not available from CygNet.");
        if (string.Equals(facilityId, NoValueWell, StringComparison.OrdinalIgnoreCase))
            throw new CygNetReadException(CygNetValues.NoValueCode, "The point exists but has not received a value yet.", retryable: true);
        if (!Points.TryGetValue(facilityId, out var points) || !points.TryGetValue(quantity, out var point))
            throw new CygNetReadException("CYGNET_POINT_NOT_FOUND", "CygNet returned no value for the point.", retryable: true);
        var now = _clock.GetUtcNow();
        return Task.FromResult(new EngineeringValue(
            quantity, point.Value, point.Unit, now.AddSeconds(-point.AgeSeconds - (profile == SimulatedCygNetProfile.DemoStale ? DemoStaleSeconds : 0)), point.Quality, Source, now));
    }
}
