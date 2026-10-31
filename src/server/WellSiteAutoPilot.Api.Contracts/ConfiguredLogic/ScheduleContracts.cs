using WellSiteAutoPilot.Api.Contracts.Executions;

namespace WellSiteAutoPilot.Api.Contracts.ConfiguredLogic;

public sealed record ScheduleTargetResponse(Guid AssetId, string? Name);

public sealed record ScheduleLastRunResponse(
    Guid ExecutionId,
    DateTimeOffset? ScheduledForUtc,
    ExecutionStatus Status,
    string? ResultCode,
    string? FailureCode);

public sealed record ScheduleResponse(
    Guid ConfiguredLogicId,
    string Name,
    Guid ActiveRevisionId,
    int RevisionNumber,
    ExecutionMode Mode,
    IReadOnlyList<ScheduleTargetResponse> Targets,
    Guid? ViewId,
    bool Enabled,
    int CadenceSeconds,
    int EffectiveCadenceSeconds,
    int MinimumCadenceSeconds,
    DateTimeOffset StartAtUtc,
    string TimeZoneId,
    DateTimeOffset? NextRunAtUtc,
    bool Running,
    ScheduleLastRunResponse? LastRun,
    bool CanManage,
    string OverlapPolicy);

/// <summary>Either field may be omitted; at least one is required. The active revision id must be sent as the If-Match header.</summary>
public sealed record UpdateScheduleRequest(int? CadenceSeconds, bool? Enabled);
