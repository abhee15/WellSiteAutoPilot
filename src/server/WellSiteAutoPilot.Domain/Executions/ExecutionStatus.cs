namespace WellSiteAutoPilot.Domain.Executions;

public enum ExecutionStatus
{
    Requested = 0,
    Queued = 1,
    Starting = 2,
    Running = 3,
    Waiting = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7,
    Suspended = 8,
    TimedOut = 9
}
