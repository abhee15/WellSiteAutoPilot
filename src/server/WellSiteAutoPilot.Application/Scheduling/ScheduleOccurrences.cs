using WellSiteAutoPilot.Domain.ConfiguredLogic;

namespace WellSiteAutoPilot.Application.Scheduling;

/// <summary>The fixed UTC cadence used by dispatch and schedule presentation.</summary>
public static class ScheduleOccurrences
{
    public static DateTimeOffset? LatestDue(ConfiguredLogicSchedule schedule, DateTimeOffset nowUtc)
    {
        if (!schedule.Enabled || schedule.CadenceSeconds <= 0) return null;
        var start = Normalize(schedule.StartAtUtc);
        var now = Normalize(nowUtc);
        if (now < start) return null;
        try
        {
            var cadence = TimeSpan.FromSeconds(schedule.CadenceSeconds).Ticks;
            return start.AddTicks((now.UtcTicks - start.UtcTicks) / cadence * cadence);
        }
        catch (OverflowException) { return null; }
    }

    /// <summary>The next future occurrence, not a promise that dispatch will start at that time.</summary>
    public static DateTimeOffset? NextAfter(ConfiguredLogicSchedule schedule, DateTimeOffset nowUtc)
    {
        if (!schedule.Enabled || schedule.CadenceSeconds <= 0) return null;
        var start = Normalize(schedule.StartAtUtc);
        if (start > Normalize(nowUtc)) return start;
        if (LatestDue(schedule, nowUtc) is not { } due) return null;
        try { return due.AddSeconds(schedule.CadenceSeconds); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static DateTimeOffset Normalize(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
}
