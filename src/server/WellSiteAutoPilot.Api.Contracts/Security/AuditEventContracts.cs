namespace WellSiteAutoPilot.Api.Contracts.Security;

/// <param name="DetailsJson">The event's recorded details as JSON text. Administrators only; may name people and reasons.</param>
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
    string DetailsJson);

/// <param name="NextCursor">Opaque token for the next (older) page, or null when this is the last page.</param>
public sealed record AuditEventPageResponse(IReadOnlyCollection<AuditEventResponse> Items, string? NextCursor);
