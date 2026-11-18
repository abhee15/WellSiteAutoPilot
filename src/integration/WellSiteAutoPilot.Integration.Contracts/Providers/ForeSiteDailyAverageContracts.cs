using System.Text.Json.Serialization;

namespace WellSiteAutoPilot.Integration.Contracts.Providers;

public sealed record ForeSiteDailyAverageRequest(
    [property: JsonRequired] string AssetExternalId,
    [property: JsonRequired] string StartUtc,
    [property: JsonRequired] string EndUtc);

public sealed record ForeSiteDailyAverageRecord(string Id, DateTimeOffset Start, DateTimeOffset End, decimal? Value)
{
    /// <summary>Every populated numeric field of this production day, by the provider's own name (includes the total rate).</summary>
    /// <remarks>Not serialized: it serves in-Gateway reads only, so the history and preview payloads keep their shape and size.</remarks>
    [JsonIgnore]
    public IReadOnlyDictionary<string, decimal>? Values { get; init; }
}
public sealed record ForeSiteDailyAverageResult(string AssetExternalId, string Quantity, string? Unit,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc, IReadOnlyList<ForeSiteDailyAverageRecord> Days,
    DateTimeOffset RetrievedAtUtc, string Source)
{
    /// <summary>Additional reviewed source fields observed with units in the requested window.</summary>
    public IReadOnlyList<ForeSiteDailyObservedQuantity>? ObservedQuantities { get; init; }

    /// <summary>The provider's unit for every populated field in the window. In-Gateway only, so it is not serialized.</summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, string>? UnitsByQuantity { get; init; }
}

public sealed record ForeSiteDailyObservedQuantity(string Quantity, string Unit);

public static class ForeSiteDailyAverageContract
{
    public const string OperationKey = "daily.inferred-total-fluid-rate";
    public const string Version = "1.0";
    public const string Quantity = "TotalFluidRateInferred";

    /// <summary>Most additional quantities one well's daily record may report as observed.</summary>
    public const int MaximumObservedQuantities = 128;

    /// <summary>Provider field names are identifiers; anything else is not a quantity and is never offered.</summary>
    public static bool IsObservableQuantityName(string? name) =>
        name is { Length: > 0 and <= 64 } && char.IsAsciiLetter(name[0]) &&
        name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_') &&
        name is not ("Id" or "WellId");
}
