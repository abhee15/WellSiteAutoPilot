using System.Collections.Concurrent;

namespace WellSiteAutoPilot.Application.Scheduling;

/// <summary>
/// Why the latest due occurrence of one configuration's schedule was not started, from the scheduler's own evaluation.
/// It is what a person needs to answer "why is this not running", not a run history: dispatched runs are Executions.
/// </summary>
/// <param name="ConfigurationRevisionId">The live revision the block was observed on; a block never outlives it.</param>
/// <param name="OccurrencesAffected">Distinct due occurrences seen with this same reason since it began.</param>
public sealed record ScheduleBlock(
    Guid ConfigurationRevisionId,
    ScheduledShadowDispatchOutcome Outcome,
    string ReasonCode,
    DateTimeOffset SinceUtc,
    DateTimeOffset LastScheduledForUtc,
    int OccurrencesAffected);

/// <summary>
/// Holds the current <see cref="ScheduleBlock"/> per configuration. The scheduler writes after every sweep and the API
/// reads. This is the seam for a durable history later: a persistent implementation can replace the in-memory one
/// without changing the scheduler, the API response or the UI.
/// </summary>
public interface IScheduleBlockStore
{
    /// <summary>
    /// Folds one sweep's decisions in. Returns true when a block opened, closed or changed reason, so callers can
    /// notify live views on a transition and stay quiet while the same block merely continues.
    /// </summary>
    bool Record(IEnumerable<ScheduledShadowDispatchDecision> decisions, DateTimeOffset nowUtc);

    IReadOnlyDictionary<Guid, ScheduleBlock> Get(IReadOnlyCollection<Guid> configuredLogicIds);
}

/// <summary>
/// Process-local store. A restart forgets blocks and the next due occurrence re-establishes them, so a block can lag by
/// at most one cadence after a restart; that is the accepted MVP trade-off for needing no schema change.
/// </summary>
public sealed class InMemoryScheduleBlockStore : IScheduleBlockStore
{
    public const string UnknownReason = "SCHEDULER_BLOCKED";

    private readonly ConcurrentDictionary<Guid, ScheduleBlock> blocks = new();

    public bool Record(IEnumerable<ScheduledShadowDispatchDecision> decisions, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        var changed = false;
        foreach (var decision in decisions)
        {
            if (IsBlocking(decision.Outcome))
            {
                changed |= Block(decision, nowUtc);
            }
            else if (decision.Outcome is ScheduledShadowDispatchOutcome.Dispatched
                     or ScheduledShadowDispatchOutcome.AlreadyDispatched)
            {
                // A run exists for the latest due occurrence, so nothing is blocking the schedule.
                changed |= blocks.TryRemove(decision.ConfiguredLogicId, out _);
            }
            else if (decision.Outcome == ScheduledShadowDispatchOutcome.NotDue &&
                     blocks.TryGetValue(decision.ConfiguredLogicId, out var current) &&
                     current.ConfigurationRevisionId != decision.ConfigurationRevisionId)
            {
                // Between occurrences a block stays; a different live revision starts clean.
                changed |= blocks.TryRemove(new KeyValuePair<Guid, ScheduleBlock>(decision.ConfiguredLogicId, current));
            }
        }

        return changed;
    }

    public IReadOnlyDictionary<Guid, ScheduleBlock> Get(IReadOnlyCollection<Guid> configuredLogicIds)
    {
        ArgumentNullException.ThrowIfNull(configuredLogicIds);
        var result = new Dictionary<Guid, ScheduleBlock>();
        foreach (var id in configuredLogicIds)
        {
            if (blocks.TryGetValue(id, out var block))
            {
                result[id] = block;
            }
        }

        return result;
    }

    // The cadence-below-minimum skip is already shown from the configuration itself, so it is not duplicated here.
    private static bool IsBlocking(ScheduledShadowDispatchOutcome outcome) => outcome is
        ScheduledShadowDispatchOutcome.OverlapBlocked or
        ScheduledShadowDispatchOutcome.ModuleUnavailable or
        ScheduledShadowDispatchOutcome.ModuleDisabled or
        ScheduledShadowDispatchOutcome.InvalidConfiguration or
        ScheduledShadowDispatchOutcome.DispatchFailed;

    private bool Block(ScheduledShadowDispatchDecision decision, DateTimeOffset nowUtc)
    {
        var reason = string.IsNullOrWhiteSpace(decision.ReasonCode) ? UnknownReason : decision.ReasonCode;
        var slot = decision.ScheduledForUtc ?? nowUtc;
        var changed = false;
        blocks.AddOrUpdate(
            decision.ConfiguredLogicId,
            _ =>
            {
                changed = true;
                return new ScheduleBlock(decision.ConfigurationRevisionId, decision.Outcome, reason, nowUtc, slot, 1);
            },
            (_, current) =>
            {
                if (current.ConfigurationRevisionId != decision.ConfigurationRevisionId || current.ReasonCode != reason)
                {
                    changed = true;
                    return new ScheduleBlock(decision.ConfigurationRevisionId, decision.Outcome, reason, nowUtc, slot, 1);
                }

                // The same occurrence is re-evaluated every sweep until the next one is due; count it once.
                return slot > current.LastScheduledForUtc
                    ? current with
                    {
                        Outcome = decision.Outcome,
                        LastScheduledForUtc = slot,
                        OccurrencesAffected = current.OccurrencesAffected + 1,
                    }
                    : current;
            });
        return changed;
    }
}
