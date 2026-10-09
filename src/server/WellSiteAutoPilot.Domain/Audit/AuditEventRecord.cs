namespace WellSiteAutoPilot.Domain.Audit;

public sealed record AuditEventRecord(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorUserId,
    string ActorIdentity,
    string Action,
    string TargetType,
    string? TargetId,
    Guid? AssetId,
    string? CorrelationId,
    string DetailsJson);
