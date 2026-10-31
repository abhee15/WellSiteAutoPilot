using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using WellSiteAutoPilot.Application.Changes;

namespace WellSiteAutoPilot.Server.Changes;

/// <summary>
/// The typed message sent as <see cref="ChangeSignalProtocol.EventMethod"/>. Sequence numbers
/// count per area stream inside one epoch (one server process run), so a user who can read only
/// some areas never sees a false gap. A new epoch or a skipped number tells the client to read
/// the area again.
/// </summary>
public sealed record ChangeEventEnvelope(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("epoch")] string Epoch,
    [property: JsonPropertyName("area")] string Area,
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("entity")] string Entity,
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("actor")] string? Actor,
    [property: JsonPropertyName("correlationId")] string? CorrelationId,
    [property: JsonPropertyName("payload")] IReadOnlyDictionary<string, string?> Payload);

public interface IChangeEventSender
{
    Task SendAsync(ChangeEventEnvelope envelope, ChangeArea area, CancellationToken cancellationToken);
}

/// <summary>Sends to the same per-area group as the area signal, so the permission check is reused as is.</summary>
public sealed class HubChangeEventSender(IHubContext<ChangesHub> hub) : IChangeEventSender
{
    public Task SendAsync(ChangeEventEnvelope envelope, ChangeArea area, CancellationToken cancellationToken) =>
        hub.Clients.Group(ChangeSignalProtocol.GroupName(area)).SendAsync(
            ChangeSignalProtocol.EventMethod, envelope, cancellationToken);
}

/// <summary>
/// Assigns per-area sequence numbers and sends events in order from one background reader, so a
/// producer only enqueues. The queue is bounded; an event dropped under overload leaves a visible
/// sequence gap, which clients answer by reading the area. Disabled unless
/// <see cref="ChangeSignalOptions.TypedEvents"/> is set. In-process only (see the design for a backplane).
/// </summary>
public sealed partial class ChangeEventService(IChangeEventSender sender, IOptions<ChangeSignalOptions> options,
    TimeProvider timeProvider, ILogger<ChangeEventService> logger) : BackgroundService, IChangeEventPublisher
{
    public const int QueueCapacity = 1024;

    private readonly Channel<(ChangeEventEnvelope Envelope, ChangeArea Area)> queue =
        Channel.CreateBounded<(ChangeEventEnvelope, ChangeArea)>(
            new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private readonly Lock gate = new();
    private readonly long[] sequences = new long[Enum.GetValues<ChangeArea>().Length];

    public string Epoch { get; } = Guid.NewGuid().ToString("N");

    public void Publish(ChangeEventDraft draft)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(draft);
            if (!options.Value.TypedEvents || !Enum.IsDefined(draft.Area)) return;
            // Numbering and enqueueing share a lock so queue order always equals sequence order.
            lock (gate)
            {
                var envelope = new ChangeEventEnvelope(1, Epoch, ChangeAreaNames.ToWireName(draft.Area),
                    ++sequences[(int)draft.Area], draft.Entity, draft.Id, draft.Kind, timeProvider.GetUtcNow(),
                    draft.Actor, draft.CorrelationId, draft.Payload ?? new Dictionary<string, string?>());
                queue.Writer.TryWrite((envelope, draft.Area));
            }
        }
        catch (Exception exception)
        {
            LogPublishFailed(logger, exception);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (envelope, area) in queue.Reader.ReadAllAsync(stoppingToken))
            await SendOneAsync(envelope, area, stoppingToken);
    }

    /// <summary>Sends what is queued right now. Public for tests that run without the hosted loop.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        while (queue.Reader.TryRead(out var item))
            await SendOneAsync(item.Envelope, item.Area, cancellationToken);
    }

    private async Task SendOneAsync(ChangeEventEnvelope envelope, ChangeArea area, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(envelope, area, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogSendFailed(logger, exception, envelope.Area, envelope.Sequence);
        }
    }

    [LoggerMessage(EventId = 2404, Level = LogLevel.Warning,
        Message = "A change event could not be queued; clients recover through the area signal.")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2405, Level = LogLevel.Warning,
        Message = "Change event {Sequence} for area {Area} could not be sent; clients detect the gap and read the area.")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, string area, long sequence);
}
