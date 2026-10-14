namespace WellSiteAutoPilot.Messaging.Nats;

/// <summary>
/// Validated durable-consumer delivery timing shared by the worker and server.
/// AckWait is a broker delivery lease, not a module execution deadline.
/// </summary>
public sealed record JetStreamConsumerSettings(
    int RequestAckWaitSeconds = 120,
    int ResultAckWaitSeconds = 30,
    int RequestProgressSeconds = 20,
    int MaxDeliver = 5)
{
    public TimeSpan RequestAckWait => TimeSpan.FromSeconds(RequestAckWaitSeconds);
    public TimeSpan ResultAckWait => TimeSpan.FromSeconds(ResultAckWaitSeconds);
    public TimeSpan RequestProgressInterval => TimeSpan.FromSeconds(RequestProgressSeconds);

    public JetStreamConsumerSettings Validate()
    {
        if (RequestAckWaitSeconds is < 10 or > 900)
        {
            throw new ArgumentOutOfRangeException(nameof(RequestAckWaitSeconds),
                "Request AckWait must be between 10 and 900 seconds.");
        }

        if (ResultAckWaitSeconds is < 10 or > 900)
        {
            throw new ArgumentOutOfRangeException(nameof(ResultAckWaitSeconds),
                "Result AckWait must be between 10 and 900 seconds.");
        }

        // Heartbeats must have ample room for network jitter and retransmission.
        if (RequestProgressSeconds < 1 ||
            RequestProgressSeconds > RequestAckWaitSeconds / 3)
        {
            throw new ArgumentOutOfRangeException(nameof(RequestProgressSeconds),
                "Request progress interval must be at least 1 second and no more than a third of AckWait.");
        }

        if (MaxDeliver is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDeliver),
                "MaxDeliver must be between 1 and 20.");
        }

        return this;
    }
}
