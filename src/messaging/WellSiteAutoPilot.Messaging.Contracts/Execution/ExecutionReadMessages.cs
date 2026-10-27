using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Messaging.Contracts.Execution;

public sealed record ExecutionReadCurrentV1(Guid ExecutionId, Guid ReadPlanId);
public sealed record ExecutionReadReplyV1(EngineeringValue? Value, string? FailureCode);
