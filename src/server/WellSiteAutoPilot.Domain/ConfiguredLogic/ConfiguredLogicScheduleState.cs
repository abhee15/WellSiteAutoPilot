namespace WellSiteAutoPilot.Domain.ConfiguredLogic;

public enum ConfiguredLogicScheduleChangeKind
{
    Paused,
    Resumed,
    FrequencyChanged,

    /// <summary>Written by revision activation, which discards live changes and returns to the schedule the revision declares.</summary>
    Reset
}

/// <summary>
/// The live schedule of a configured logic when it differs from what its active revision declares. A saved revision is
/// immutable, so a pause or a frequency change is recorded here instead. <see cref="Version"/> counts changes made while
/// <see cref="BasedOnRevisionId"/> was the active revision; activating another revision discards the state.
/// </summary>
public sealed record ConfiguredLogicScheduleState(
    Guid ConfiguredLogicId,
    Guid BasedOnRevisionId,
    int Version,
    ConfiguredLogicSchedule Schedule,
    string ChangedBy,
    DateTimeOffset ChangedAtUtc,
    string? Reason);

/// <summary>One append-only entry in the history of a configured logic's live schedule.</summary>
public sealed record ConfiguredLogicScheduleChange(
    Guid Id,
    Guid ConfiguredLogicId,
    Guid BasedOnRevisionId,
    int Version,
    ConfiguredLogicScheduleChangeKind Kind,
    ConfiguredLogicSchedule Schedule,
    string ChangedBy,
    DateTimeOffset ChangedAtUtc,
    string? Reason);
