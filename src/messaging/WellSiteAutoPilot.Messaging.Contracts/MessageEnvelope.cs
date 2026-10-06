namespace WellSiteAutoPilot.Messaging.Contracts;

public sealed record MessageEnvelope<TPayload>(
    Guid MessageId,
    string MessageType,
    int ContractVersion,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? TraceParent,
    TPayload Payload);
