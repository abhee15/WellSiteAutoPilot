namespace WellSiteAutoPilot.Persistence.Connections;

/// <summary>
/// Remembers which object in an outside system an Asset was made from. The key is the connection, the kind of
/// object and the system's own id; names are never used to match. <see cref="SnapshotJson"/> keeps what the system
/// said at import time.
/// </summary>
public sealed class SourceBindingEntity
{
    public Guid Id { get; init; }
    public Guid ConnectionId { get; init; }
    public string ResourceKind { get; init; } = string.Empty;
    public string ExternalId { get; init; } = string.Empty;
    public Guid AssetId { get; init; }
    public string SnapshotJson { get; init; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; init; }
}
