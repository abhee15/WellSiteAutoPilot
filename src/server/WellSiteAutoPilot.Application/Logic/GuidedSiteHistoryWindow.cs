using System.Globalization;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>A trailing elapsed interval ending at an explicitly chosen site-local minute.</summary>
public sealed record GuidedSiteHistoryWindow(string EndLocal, string TimeZoneId, int DurationMinutes)
{
    internal static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) Resolve(DateTimeOffset? startUtc,
        DateTimeOffset? endUtc, GuidedSiteHistoryWindow? siteWindow)
    {
        if (siteWindow is null)
        {
            if (startUtc is { } start && endUtc is { } end) return (start, end);
            throw InvalidWindow();
        }

        if (startUtc is not null || endUtc is not null ||
            siteWindow.EndLocal is not { Length: 16 } || siteWindow.TimeZoneId is not { Length: > 0 and <= 128 } ||
            siteWindow.DurationMinutes is < 1 or > 1440 ||
            !DateTime.TryParseExact(siteWindow.EndLocal, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var wallTime))
            throw InvalidWindow();

        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(siteWindow.TimeZoneId); }
        catch (TimeZoneNotFoundException) { throw InvalidZone(); }
        catch (InvalidTimeZoneException) { throw InvalidZone(); }

        if (zone.IsInvalidTime(wallTime) || zone.IsAmbiguousTime(wallTime))
            throw new WellSiteAutoPilotException("GUIDED_HISTORY_SITE_TIME_UNRESOLVED", FailureKind.Validation,
                "That local time does not identify a single instant in the site time zone. Choose another time or use UTC bounds.");

        try
        {
            var end = new DateTimeOffset(wallTime, zone.GetUtcOffset(wallTime)).ToUniversalTime();
            return (end.AddMinutes(-siteWindow.DurationMinutes), end);
        }
        catch (ArgumentException) { throw InvalidWindow(); }
    }

    private static WellSiteAutoPilotException InvalidWindow() =>
        new("GUIDED_HISTORY_WINDOW_INVALID", FailureKind.Validation,
            "Provide either both UTC bounds or a site-local end time and duration.");

    private static WellSiteAutoPilotException InvalidZone() =>
        new("GUIDED_HISTORY_SITE_ZONE_INVALID", FailureKind.Validation, "Choose a recognized site time zone.");
}
