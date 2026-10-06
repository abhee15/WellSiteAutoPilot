namespace WellSiteAutoPilot.Persistence.Messaging;

public sealed class InboxMessageEntity
{
    public Guid MessageId { get; init; }

    public string Consumer { get; init; } = string.Empty;

    public DateTimeOffset ProcessedAtUtc { get; init; }
}
