using System.Text.Json.Serialization;
using WellSiteAutoPilot.Api.Contracts.Serialization;

namespace WellSiteAutoPilot.Api.Contracts.Executions;

// Closed wire vocabulary. Member names are the JSON values; numeric values are
// private to the contract and 0 is deliberately undefined.
[JsonConverter(typeof(ClosedStringEnumConverter<ExecutionMode>))]
public enum ExecutionMode
{
    Shadow = 1,
    Recommendation = 2,
    Autonomous = 3
}

[JsonConverter(typeof(ClosedStringEnumConverter<ExecutionStatus>))]
public enum ExecutionStatus
{
    Requested = 1,
    Queued = 2,
    Starting = 3,
    Running = 4,
    Waiting = 5,
    Completed = 6,
    Failed = 7,
    Cancelled = 8,
    Suspended = 9,
    TimedOut = 10
}

[JsonConverter(typeof(ClosedStringEnumConverter<ExecutionTriggerKind>))]
public enum ExecutionTriggerKind
{
    Manual = 1,
    Scheduled = 2
}
