namespace WellSiteAutoPilot.Application.Changes;

/// <summary>Stable entity and kind names carried by typed change events (docs/design/realtime-events.md).</summary>
public static class ChangeEventNames
{
    public const string Execution = "execution";
    public const string Recommendation = "recommendation";

    public const string Requested = "requested";
    public const string Started = "started";
    public const string Progress = "progress";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Created = "created";
    public const string Decided = "decided";
}

/// <summary>
/// One entity change, as a producer describes it. The publisher adds the sequence, epoch and
/// time. Payload entries are limited to short state labels (status, result or failure code);
/// anything richer is read through the authorized REST endpoints.
/// </summary>
public sealed record ChangeEventDraft(
    ChangeArea Area,
    string Entity,
    Guid Id,
    string Kind,
    string? Actor = null,
    string? CorrelationId = null,
    IReadOnlyDictionary<string, string?>? Payload = null);

/// <summary>
/// Typed companion to <see cref="IChangeNotifier"/>. A producer keeps its existing area
/// notification and additionally publishes the event, so clients that only understand area
/// signals are unaffected. Implementations are best effort and never throw or block.
/// </summary>
public interface IChangeEventPublisher
{
    void Publish(ChangeEventDraft draft);
}

public sealed class NoOpChangeEventPublisher : IChangeEventPublisher
{
    public static NoOpChangeEventPublisher Instance { get; } = new();

    private NoOpChangeEventPublisher()
    {
    }

    public void Publish(ChangeEventDraft draft)
    {
    }
}
