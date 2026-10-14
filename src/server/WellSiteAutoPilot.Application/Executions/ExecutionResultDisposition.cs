namespace WellSiteAutoPilot.Application.Executions;

/// <summary>
/// The persisted disposition of one incoming worker result.
/// An Inbox replay is distinct from an independently delivered terminal result.
/// </summary>
public enum ExecutionResultDisposition
{
    Applied = 0,
    AlreadyProcessed = 1,
    MatchingTerminalResult = 2,
    ConflictingTerminalResult = 3
}
