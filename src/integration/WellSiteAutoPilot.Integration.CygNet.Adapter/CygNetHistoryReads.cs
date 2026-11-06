using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>One calendar day of a point's history in the asset's time zone. Unreliable entries are left out of the average.</summary>
public sealed record CygNetDailyAverage(DateOnly Day, double Average, int Samples, int UnreliableSkipped);

/// <summary>
/// Range and daily reads over CygNet history. The window is bounded, the point is named by UDC, and a "day" is a calendar
/// day in the asset's time zone, because CygNet stores UTC and a day cut at UTC midnight is wrong for a well in Texas.
/// </summary>
public static class CygNetHistoryReads
{
    public static readonly TimeSpan MaximumWindow = TimeSpan.FromDays(31);

    public static Task<CygNetHistory> ReadRangeAsync(ICygNetClient client, CygNetConnection connection, string facilityId, string udc,
        DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(connection);
        if (!CygNetCatalog.IsUdc(udc))
            throw new CygNetReadException("CYGNET_POINT_NOT_FOUND", "The point name is not a valid UDC.");
        if (endUtc <= startUtc || endUtc - startUtc > MaximumWindow)
            throw new CygNetReadException("CYGNET_HISTORY_WINDOW_INVALID", "The history window must end after it starts and span at most 31 days.");
        return client.ReadHistoryAsync(connection, facilityId, udc.ToUpperInvariant(), startUtc, endUtc, cancellationToken);
    }

    public static async Task<IReadOnlyList<CygNetDailyAverage>> ReadDailyAveragesAsync(ICygNetClient client, CygNetConnection connection,
        string facilityId, string udc, DateOnly firstDay, DateOnly lastDay, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (lastDay < firstDay)
            throw new CygNetReadException("CYGNET_HISTORY_WINDOW_INVALID", "The last day is before the first day.");
        var start = LocalMidnightUtc(firstDay, zone);
        var end = LocalMidnightUtc(lastDay.AddDays(1), zone);
        var history = await ReadRangeAsync(client, connection, facilityId, udc, start, end, cancellationToken);
        if (history.Truncated)
            throw new CygNetReadException("CYGNET_HISTORY_TRUNCATED", "CygNet returned too many entries to average the window.");
        return history.Entries
            .Where(e => e.Timestamp >= start && e.Timestamp < end)
            .GroupBy(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.Timestamp, zone).DateTime))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var good = g.Where(e => !e.Unreliable).ToList();
                return new CygNetDailyAverage(g.Key, good.Count == 0 ? double.NaN : good.Average(e => e.Value), good.Count, g.Count() - good.Count);
            })
            .ToList();
    }

    private static DateTimeOffset LocalMidnightUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        // A midnight skipped by a spring-forward change starts the day at the first valid instant after it.
        if (zone.IsInvalidTime(local)) local = local.AddHours(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}
