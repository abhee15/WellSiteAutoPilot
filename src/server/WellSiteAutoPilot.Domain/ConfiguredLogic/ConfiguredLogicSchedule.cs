namespace WellSiteAutoPilot.Domain.ConfiguredLogic;

public sealed record ConfiguredLogicSchedule(
    bool Enabled,
    int CadenceSeconds,
    DateTimeOffset StartAtUtc,
    string TimeZoneId)
{
    public ConfiguredLogicRecurrence? Recurrence { get; init; }
    public DateTimeOffset? EndAtUtc { get; init; }
    public DateTimeOffset? ResumeAfterUtc { get; init; }
}

public sealed record ConfiguredLogicRecurrence(int Every, string Unit, bool SkipMissingMonthDay = false);
