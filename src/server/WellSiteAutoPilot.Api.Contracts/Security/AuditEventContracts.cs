namespace WellSiteAutoPilot.Api.Contracts.Security;

/// <param name="DetailsJson">The event's recorded details as JSON text. Administrators only; may name people and reasons.</param>
/// <param name="TargetName">The current name of the affected record, or null when it has none or no longer exists.</param>
/// <param name="ActorName">The display name of the person who acted, or null for system events or unknown people.</param>
public sealed record AuditEventResponse(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    Guid? ActorUserId,
    string ActorIdentity,
    string Action,
    string TargetType,
    string? TargetId,
    Guid? AssetId,
    string? CorrelationId,
    string DetailsJson,
    string? TargetName = null,
    string? ActorName = null);

/// <param name="NextCursor">Opaque token for the next (older) page, or null when this is the last page.</param>
public sealed record AuditEventPageResponse(IReadOnlyCollection<AuditEventResponse> Items, string? NextCursor);
