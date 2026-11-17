using System.Text.Json.Serialization;

namespace WellSiteAutoPilot.Integration.Contracts.Providers;

public sealed record ForeSiteDailyAverageRequest(
    [property: JsonRequired] string AssetExternalId,
    [property: JsonRequired] string StartUtc,
    [property: JsonRequired] string EndUtc);

public sealed record ForeSiteDailyAverageRecord(string Id, DateTimeOffset Start, DateTimeOffset End, decimal? Value);
public sealed record ForeSiteDailyAverageResult(string AssetExternalId, string Quantity, string? Unit,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc, IReadOnlyList<ForeSiteDailyAverageRecord> Days,
    DateTimeOffset RetrievedAtUtc, string Source)
{
    /// <summary>Additional reviewed source fields observed with units in the requested window.</summary>
    public IReadOnlyList<ForeSiteDailyObservedQuantity>? ObservedQuantities { get; init; }
}

public sealed record ForeSiteDailyObservedQuantity(string Quantity, string Unit);

public static class ForeSiteDailyAverageContract
{
    public const string OperationKey = "daily.inferred-total-fluid-rate";
    public const string Version = "1.0";
    public const string Quantity = "TotalFluidRateInferred";

    public static IReadOnlyList<string> InferredPhaseRates { get; } = Array.AsReadOnly(new[]
    {
        "OilRateInferred", "GasRateInferred", "WaterRateInferred"
    });
}
