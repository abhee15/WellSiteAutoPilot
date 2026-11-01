using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>
/// A deterministic stand-in for a CygNet facility service, for development, demonstration and tests. It serves the same
/// facilities and points for any domain, site and service, never touches the network, and stamps values relative to the
/// supplied clock so freshness rules behave. Values are fixed per well so module outcomes are repeatable:
/// WELL_001 healthy, WELL_002 low fillage, WELL_003 high casing-tubing differential, WELL_004 high motor current,
/// WELL_005 uncertain and stale.
/// </summary>
public sealed class SimulatedCygNetClient(TimeProvider? clock = null) : ICygNetClient
{
    public const string Source = "cygnet-simulated";

    private sealed record Point(decimal Value, string Unit, string Quality = "Good", int AgeSeconds = 5);

    private static readonly CygNetFacility[] Facilities =
    [
        new("BATTERY_A", "Pad A battery", "BATTERY", "Pad A"),
        new("WELL_001", "Pad A well 1 (healthy)", "WELL", "Pad A"),
        new("WELL_002", "Pad A well 2 (pumped off)", "WELL", "Pad A"),
        new("WELL_003", "Pad A well 3 (high casing pressure)", "WELL", "Pad A"),
        new("WELL_004", "Pad B well 4 (high motor current)", "WELL", "Pad B"),
        new("WELL_005", "Pad B well 5 (stale, uncertain data)", "WELL", "Pad B"),
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

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task CheckAsync(CygNetConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CygNetFacilityList(Facilities));
    }

    public Task<EngineeringValue> ReadCurrentAsync(
        CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AdapterReadRules.CygNetQuantities.Contains(quantity))
            throw new CygNetReadException("CYGNET_QUANTITY_UNSUPPORTED", "This quantity is not available from CygNet.");
        if (!Points.TryGetValue(facilityId, out var points) || !points.TryGetValue(quantity, out var point))
            throw new CygNetReadException("CYGNET_POINT_NOT_FOUND", "CygNet returned no value for the point.", retryable: true);
        var now = _clock.GetUtcNow();
        return Task.FromResult(new EngineeringValue(
            quantity, point.Value, point.Unit, now.AddSeconds(-point.AgeSeconds), point.Quality, Source, now));
    }
}
