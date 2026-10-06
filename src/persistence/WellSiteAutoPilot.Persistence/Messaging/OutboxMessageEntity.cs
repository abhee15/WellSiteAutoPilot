namespace WellSiteAutoPilot.Persistence.Messaging;

public sealed class OutboxMessageEntity
{
    public Guid Id { get; init; }

    public string Type { get; init; } = string.Empty;

    public string Payload { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }
}
