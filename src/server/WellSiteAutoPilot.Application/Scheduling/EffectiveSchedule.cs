using WellSiteAutoPilot.Domain.ConfiguredLogic;

namespace WellSiteAutoPilot.Application.Scheduling;

/// <summary>
/// The one place that decides which schedule a configured logic is running on. A saved revision is immutable, so a person's
/// pause or frequency change lives beside it as live state; the scheduler and every next-run display must read the
/// result of this resolver and never the revision's own schedule, or they would disagree about a paused logic.
/// </summary>
public static class EffectiveSchedule
{
    /// <summary>
    /// The live state when it was made against the active revision, otherwise what that revision declares. State left over
    /// from an earlier revision is ignored rather than trusted, so a missed reset can never run the wrong cadence.
    /// </summary>
    public static ConfiguredLogicSchedule? Resolve(
        ConfiguredLogicRevision? activeRevision,
        ConfiguredLogicScheduleState? state)
    {
        if (activeRevision is null) return null;
        return state is not null && state.BasedOnRevisionId == activeRevision.Id
            ? state.Schedule
            : activeRevision.Schedule;
    }

    public static DateTimeOffset? NextRunAt(
        ConfiguredLogicRevision? activeRevision,
        ConfiguredLogicScheduleState? state,
        DateTimeOffset nowUtc) =>
        Resolve(activeRevision, state) is { } schedule ? ScheduleOccurrences.NextAfter(schedule, nowUtc) : null;
}
