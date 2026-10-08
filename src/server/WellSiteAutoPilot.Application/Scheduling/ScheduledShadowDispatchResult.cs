namespace WellSiteAutoPilot.Application.Scheduling;

public enum ScheduledShadowDispatchOutcome
{
    Dispatched = 0,
    NotDue = 1,
    AlreadyDispatched = 2,
    OverlapBlocked = 3,
    ModuleUnavailable = 4,
    ModuleDisabled = 5,
    InvalidConfiguration = 6
}

public sealed record ScheduledShadowDispatchDecision(
    Guid ConfiguredLogicId,
    Guid ConfigurationRevisionId,
    DateTimeOffset? ScheduledForUtc,
    ScheduledShadowDispatchOutcome Outcome,
    string? ReasonCode,
    Guid? ExecutionId);

public sealed record ScheduledShadowSweepResult(
    int CandidateCount,
    IReadOnlyCollection<ScheduledShadowDispatchDecision> Decisions);
