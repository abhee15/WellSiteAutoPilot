using System.Text.Json.Serialization;
using WellSiteAutoPilot.Api.Contracts.Serialization;

namespace WellSiteAutoPilot.Api.Contracts.Logic;

// Closed wire vocabulary. Member names are the JSON values; numeric values are
// private to the contract and 0 is deliberately undefined.
[JsonConverter(typeof(ClosedStringEnumConverter<LogicRuntimeKind>))]
public enum LogicRuntimeKind
{
    Python = 1,
    DotNet = 2
}

[JsonConverter(typeof(ClosedStringEnumConverter<ExecutionProfile>))]
public enum ExecutionProfile
{
    ScheduledOneShot = 1,
    BoundedIterative = 2,
    Reactive = 3,
    ContinuousController = 4,
    DurableWorkflow = 5
}

[JsonConverter(typeof(ClosedStringEnumConverter<LogicModuleTrustStatus>))]
public enum LogicModuleTrustStatus
{
    Untrusted = 1,
    TrustedPublisher = 2,
    CustomerApproved = 3,
    SignatureInvalid = 4
}
