using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Scheduling;

/// <summary>Shared occurrence calculation for dispatch, review and next-run presentation.</summary>
public static class ScheduleOccurrences
{
    public static IReadOnlyList<DateTimeOffset> Preview(ConfiguredLogicSchedule schedule)
    {
        Validate(schedule);
        var occurrences = new List<DateTimeOffset>();
        var after = schedule.StartAtUtc == DateTimeOffset.MinValue ? schedule.StartAtUtc : schedule.StartAtUtc.AddTicks(-10);
        for (var index = 0; index < 3; index++)
        {
            if (NextAfter(schedule, after) is not { } next) break;
            occurrences.Add(next);
            after = next;
        }
        return occurrences;
    }

    public static void Validate(ConfiguredLogicSchedule schedule)
    {
        if (schedule.CadenceSeconds <= 0) throw Invalid("The schedule interval must be positive.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId); }
        catch (TimeZoneNotFoundException) { throw Invalid("Choose a recognized time zone."); }
        catch (InvalidTimeZoneException) { throw Invalid("Choose a valid time zone."); }
        if (schedule.EndAtUtc is { } end && end < schedule.StartAtUtc)
            throw Invalid("The end must be on or after the start.");
        if (schedule.Recurrence is not { } recurrence) return;
        if (recurrence.Every is < 1 or > 999)
            throw Invalid("The repeat interval must be a whole number from 1 to 999.");
        var seconds = recurrence.Unit switch
        {
            "Seconds" => 1, "Minutes" => 60, "Hours" => 3600,
            "Days" => 86400, "Weeks" => 604800, "Months" => 2419200,
            _ => throw Invalid("The schedule frequency unit is unsupported.")
        };
        if (checked((long)seconds * recurrence.Every) > int.MaxValue ||
            schedule.CadenceSeconds != (long)seconds * recurrence.Every)
            throw Invalid("The schedule interval does not match its frequency.");
    }

    public static DateTimeOffset? LatestDue(ConfiguredLogicSchedule schedule, DateTimeOffset nowUtc)
    {
        if (!schedule.Enabled || schedule.CadenceSeconds <= 0 ||
            schedule.EndAtUtc is { } end && nowUtc > end) return null;
        var start = Normalize(schedule.StartAtUtc);
        var now = Normalize(nowUtc);
        if (now < start) return null;
        if (IsCalendar(schedule))
        {
            var index = Estimate(schedule, now);
            for (; index >= 0; index--)
                if (CalendarOccurrence(schedule, index) is { } occurrence && occurrence <= now)
                    return AfterResume(schedule, occurrence);
            return null;
        }
        try
        {
            var cadence = TimeSpan.FromSeconds(schedule.CadenceSeconds).Ticks;
            return AfterResume(schedule, start.AddTicks((now.UtcTicks - start.UtcTicks) / cadence * cadence));
        }
        catch (OverflowException) { return null; }
    }

    public static DateTimeOffset? NextAfter(ConfiguredLogicSchedule schedule, DateTimeOffset nowUtc)
    {
        if (schedule.ResumeAfterUtc is { } resumed && nowUtc < resumed) nowUtc = resumed;
        if (!schedule.Enabled || schedule.CadenceSeconds <= 0 ||
            schedule.EndAtUtc is { } end && nowUtc >= end) return null;
        var start = Normalize(schedule.StartAtUtc);
        if (start > Normalize(nowUtc)) return WithinEnd(schedule, start);
        if (IsCalendar(schedule))
        {
            for (var index = Math.Max(0, Estimate(schedule, nowUtc) - 1); ; index++)
            {
                var occurrence = CalendarOccurrence(schedule, index, out var exhausted);
                if (exhausted) return null;
                if (occurrence is { } next && next > nowUtc) return WithinEnd(schedule, next);
            }
        }
        if (LatestDue(schedule with { ResumeAfterUtc = null }, nowUtc) is not { } due) return null;
        try { return WithinEnd(schedule, due.AddSeconds(schedule.CadenceSeconds)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static bool IsCalendar(ConfiguredLogicSchedule schedule) =>
        schedule.Recurrence?.Unit is "Days" or "Weeks" or "Months";

    private static long Estimate(ConfiguredLogicSchedule schedule, DateTimeOffset nowUtc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        var start = TimeZoneInfo.ConvertTime(schedule.StartAtUtc, zone).DateTime;
        var now = TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime;
        var recurrence = schedule.Recurrence!;
        return recurrence.Unit == "Months"
            ? Math.Max(0, ((now.Year - start.Year) * 12L + now.Month - start.Month) / recurrence.Every)
            : Math.Max(0, (long)(now.Date - start.Date).TotalDays /
                (recurrence.Every * (recurrence.Unit == "Weeks" ? 7L : 1)));
    }

    private static DateTimeOffset? CalendarOccurrence(ConfiguredLogicSchedule schedule, long index) =>
        CalendarOccurrence(schedule, index, out _);

    private static DateTimeOffset? CalendarOccurrence(ConfiguredLogicSchedule schedule, long index, out bool exhausted)
    {
        exhausted = false;
        if (index == 0) return Normalize(schedule.StartAtUtc);
        var recurrence = schedule.Recurrence!;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
        var start = TimeZoneInfo.ConvertTime(schedule.StartAtUtc, zone).DateTime;
        DateTime local;
        try
        {
            local = recurrence.Unit == "Months" ? start.AddMonths(checked((int)(index * recurrence.Every)))
                : start.AddDays(index * recurrence.Every * (recurrence.Unit == "Weeks" ? 7L : 1));
        }
        catch (ArgumentOutOfRangeException) { exhausted = true; return null; }
        catch (OverflowException) { exhausted = true; return null; }
        if (recurrence.Unit == "Months" && recurrence.SkipMissingMonthDay && local.Day != start.Day) return null;
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) return null;
        // The earlier UTC instant is the single occurrence when a local clock time repeats.
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private static DateTimeOffset? WithinEnd(ConfiguredLogicSchedule schedule, DateTimeOffset value) =>
        schedule.EndAtUtc is { } end && value > end ? null : value;

    private static DateTimeOffset? AfterResume(ConfiguredLogicSchedule schedule, DateTimeOffset value) =>
        schedule.ResumeAfterUtc is { } resumed && value <= resumed ? null : value;

    private static DateTimeOffset Normalize(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);

    private static WellSiteAutoPilotException Invalid(string message) =>
        new("CONFIGURED_LOGIC_SCHEDULE_INVALID", FailureKind.Validation, message);
}
