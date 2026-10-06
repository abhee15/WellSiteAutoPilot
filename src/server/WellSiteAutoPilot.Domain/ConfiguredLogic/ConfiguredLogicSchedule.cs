namespace WellSiteAutoPilot.Domain.ConfiguredLogic;

public sealed record ConfiguredLogicSchedule(
    bool Enabled,
    int CadenceSeconds,
    DateTimeOffset StartAtUtc,
    string TimeZoneId);
