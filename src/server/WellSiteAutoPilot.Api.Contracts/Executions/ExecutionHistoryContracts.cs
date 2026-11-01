namespace WellSiteAutoPilot.Api.Contracts.Executions;

public sealed record ExecutionHistoryResponse(ExecutionResponse[] Items, Guid? NextCursor);
