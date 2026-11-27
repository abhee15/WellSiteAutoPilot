namespace WellSiteAutoPilot.Application.Scheduling;

/// <summary>The latest scheduled start that could not run, and why.</summary>
public sealed record ScheduledDispatchBlock(
    Guid ConfiguredLogicId,
    Guid ConfigurationRevisionId,
    DateTimeOffset? ScheduledForUtc,
    DateTimeOffset ObservedAtUtc,
    string ReasonCode);

/// <summary>
/// Remembers, per logic, the last scheduled start the scheduler refused. Without it a blocked schedule looks healthy
/// ("running every 5 minutes") while nothing runs. Held in memory: it answers "is it blocked right now", and the next
/// sweep restores it within seconds after a restart.
/// </summary>
public interface IScheduledDispatchBlockLog
{
    void Record(IEnumerable<ScheduledShadowDispatchDecision> decisions, DateTimeOffset observedAtUtc);

    ScheduledDispatchBlock? Latest(Guid configuredLogicId, Guid? activeRevisionId);
}

public sealed class ScheduledDispatchBlockLog : IScheduledDispatchBlockLog
{
    private readonly global::System.Collections.Concurrent.ConcurrentDictionary<Guid, ScheduledDispatchBlock> blocks = new();

    public void Record(IEnumerable<ScheduledShadowDispatchDecision> decisions, DateTimeOffset observedAtUtc)
    {
        foreach (var decision in decisions)
        {
            switch (decision.Outcome)
            {
                case ScheduledShadowDispatchOutcome.Dispatched:
                    blocks.TryRemove(decision.ConfiguredLogicId, out _);
                    break;
                case ScheduledShadowDispatchOutcome.NotDue or ScheduledShadowDispatchOutcome.AlreadyDispatched:
                    break;
                default:
                    if (decision.ReasonCode is { } reason)
                    {
                        blocks[decision.ConfiguredLogicId] = new ScheduledDispatchBlock(
                            decision.ConfiguredLogicId, decision.ConfigurationRevisionId,
                            decision.ScheduledForUtc, observedAtUtc, reason);
                    }
                    break;
            }
        }
    }

    public ScheduledDispatchBlock? Latest(Guid configuredLogicId, Guid? activeRevisionId) =>
        blocks.TryGetValue(configuredLogicId, out var block) &&
        (activeRevisionId is null || block.ConfigurationRevisionId == activeRevisionId)
            ? block : null;
}
