namespace WellSiteAutoPilot.Persistence.Audit;

public sealed class AuditEventEntity
{
    public Guid Id { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public Guid? ActorUserId { get; init; }
    public string ActorIdentity { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string TargetType { get; init; } = string.Empty;
    public string? TargetId { get; init; }
    public Guid? AssetId { get; init; }
    public string? CorrelationId { get; init; }
    public string DetailsJson { get; init; } = "{}";
}
