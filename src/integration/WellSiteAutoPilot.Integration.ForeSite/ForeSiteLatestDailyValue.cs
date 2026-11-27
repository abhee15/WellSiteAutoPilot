using System.Globalization;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>
/// One provider daily field for the latest production day that has finished ("yesterday"), as a scalar reading.
/// The production day is the provider's own record: its start and end are never recomputed from a clock cutoff here.
/// </summary>
public static class ForeSiteLatestDailyValue
{
    public const string Source = "foresite:daily-averages";
    public const string TimestampKind = "ProductionDayEnd";

    /// <summary>How far back to look for the latest finished day. Wider than a day so a late-closing record is still found.</summary>
    public static readonly TimeSpan Lookback = TimeSpan.FromDays(4);

    public static async Task<EngineeringValue> ReadAsync(ForeSiteSession session, string externalId, string field,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        if (!ForeSiteDailyAverageContract.IsObservableQuantityName(field))
            throw new ForeSiteMeasurementException("FORESITE_QUANTITY_UNSUPPORTED");
        var now = clock.GetUtcNow();
        static string Text(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var result = await new ForeSiteDailyAverageReader(session)
            .ReadAsync(new(externalId, Text(now - Lookback), Text(now)), cancellationToken);
        return Select(result, field, now);
    }

    /// <summary>The finished day with the latest start that reports the field. A day still in progress is never used.</summary>
    public static EngineeringValue Select(ForeSiteDailyAverageResult result, string field, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);
        var day = result.Days
            .Where(candidate => candidate.End <= now && candidate.Values?.ContainsKey(field) == true)
            .OrderByDescending(candidate => candidate.Start)
            .FirstOrDefault() ?? throw new ForeSiteMeasurementException("FORESITE_MEASUREMENT_MISSING");
        var unit = result.UnitsByQuantity?.GetValueOrDefault(field);
        if (string.IsNullOrWhiteSpace(unit) && ForeSiteDailyCatalogue.Find(field) is { ProviderFixedUnit: true } fixedUnit)
            unit = fixedUnit.Unit;
        if (string.IsNullOrWhiteSpace(unit)) throw new ForeSiteMeasurementException("FORESITE_DAILY_UNIT_MISSING");
        // The daily record carries no verified quality, so the reading says so and a module must accept uncertain data.
        return new(field, day.Values![field], unit, day.End, "Uncertain", Source, result.RetrievedAtUtc, TimestampKind);
    }
}
