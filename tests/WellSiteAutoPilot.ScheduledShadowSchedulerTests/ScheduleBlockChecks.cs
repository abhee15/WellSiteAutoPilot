using WellSiteAutoPilot.Application.Scheduling;

internal static class ScheduleBlockChecks
{
    public static void Run()
    {
        var configuration = Guid.NewGuid();
        var revision = Guid.NewGuid();
        var t0 = DateTimeOffset.Parse("2027-01-01T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var store = new InMemoryScheduleBlockStore();

        ScheduledShadowDispatchDecision Decision(Guid rev, ScheduledShadowDispatchOutcome outcome, string? reason, DateTimeOffset? slot) =>
            new(configuration, rev, slot, outcome, reason, null);

        Require(store.GetBlocks([configuration]).Count == 0, "Nothing is blocked at first");
        Require(!store.Record([Decision(revision, ScheduledShadowDispatchOutcome.NotDue, null, null)], t0), "Not due alone changes nothing");

        // A refusal opens a block with its reason and the occurrence it was seen on.
        var refused = Decision(revision, ScheduledShadowDispatchOutcome.InvalidConfiguration, "EXECUTION_READ_AUTHORITY_REQUIRED", t0);
        Require(store.Record([refused], t0.AddSeconds(1)), "A refusal opens a block");
        var block = store.GetBlocks([configuration])[configuration];
        Require(block.ReasonCode == "EXECUTION_READ_AUTHORITY_REQUIRED" && block.OccurrencesAffected == 1 &&
                block.SinceUtc == t0.AddSeconds(1) && block.ConfigurationRevisionId == revision, "Block carries reason, start and revision");

        // The same occurrence is re-evaluated every sweep: it is neither a change nor counted again.
        Require(!store.Record([refused], t0.AddSeconds(6)), "Re-evaluating one occurrence is not a transition");
        Require(store.GetBlocks([configuration])[configuration].OccurrencesAffected == 1, "One occurrence counts once");

        // The next occurrence with the same reason continues the block, keeping when it began.
        var next = Decision(revision, ScheduledShadowDispatchOutcome.InvalidConfiguration, "EXECUTION_READ_AUTHORITY_REQUIRED", t0.AddMinutes(1));
        Require(!store.Record([next], t0.AddMinutes(1)), "The same reason continuing is not a transition");
        block = store.GetBlocks([configuration])[configuration];
        Require(block.OccurrencesAffected == 2 && block.SinceUtc == t0.AddSeconds(1) && block.LastScheduledForUtc == t0.AddMinutes(1), "Continuing counts and keeps the start");

        // Between occurrences the block stays, so a quiet sweep does not hide a problem.
        Require(!store.Record([Decision(revision, ScheduledShadowDispatchOutcome.NotDue, null, null)], t0.AddMinutes(1).AddSeconds(5)) &&
                store.GetBlocks([configuration]).Count == 1, "Not due keeps the block");

        // A different reason replaces it and counts as a transition.
        Require(store.Record([Decision(revision, ScheduledShadowDispatchOutcome.ModuleDisabled, "SCHEDULER_MODULE_NOT_ENABLED", t0.AddMinutes(2))], t0.AddMinutes(2)) &&
                store.GetBlocks([configuration])[configuration] is { ReasonCode: "SCHEDULER_MODULE_NOT_ENABLED", OccurrencesAffected: 1 }, "A new reason restarts the block");

        // A run that started clears it.
        var dispatched = Decision(revision, ScheduledShadowDispatchOutcome.Dispatched, null, t0.AddMinutes(3));
        Require(store.Record([dispatched], t0.AddMinutes(3)) && store.GetBlocks([configuration]).Count == 0, "Dispatch clears the block");
        Require(!store.Record([dispatched], t0.AddMinutes(3).AddSeconds(5)), "Clearing an absent block is not a transition");

        // A new live revision starts clean, even before it is due.
        store.Record([Decision(revision, ScheduledShadowDispatchOutcome.OverlapBlocked, "SCHEDULER_EXECUTION_OVERLAP", t0.AddMinutes(4))], t0.AddMinutes(4));
        var newer = Guid.NewGuid();
        Require(store.Record([Decision(newer, ScheduledShadowDispatchOutcome.NotDue, null, null)], t0.AddMinutes(5)) &&
                store.GetBlocks([configuration]).Count == 0, "Another revision does not inherit a block");

        // Missing reason codes still produce a block a person can see; a below-minimum skip is shown elsewhere.
        Require(store.Record([Decision(revision, ScheduledShadowDispatchOutcome.DispatchFailed, null, null)], t0.AddMinutes(6)) &&
                store.GetBlocks([configuration])[configuration].ReasonCode == InMemoryScheduleBlockStore.UnknownReason, "A missing reason is named");
        store.Record([Decision(revision, ScheduledShadowDispatchOutcome.Dispatched, null, t0.AddMinutes(7))], t0.AddMinutes(7));
        Require(!store.Record([Decision(revision, ScheduledShadowDispatchOutcome.CadenceBelowMinimum, "SCHEDULER_CADENCE_BELOW_MINIMUM", t0.AddMinutes(8))], t0.AddMinutes(8)) &&
                store.GetBlocks([configuration]).Count == 0, "Below minimum is not duplicated as a block");
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }
}
