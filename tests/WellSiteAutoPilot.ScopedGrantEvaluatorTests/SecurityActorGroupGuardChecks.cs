using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;

internal static class SecurityActorGroupGuardChecks
{
    public static void Run()
    {
        var now = new DateTimeOffset(2026, 10, 16, 17, 0, 0, TimeSpan.Zero);
        var id = Guid.NewGuid();
        const string first = "S-1-5-21-1-2-3-4";
        const string second = "S-1-5-21-1-2-3-5";
        const string other = "S-1-5-21-1-2-3-6";
        var actor = new SecurityActorContext(id, "DOMAIN\\admin", "own-group")
        {
            RequiresGroupProof = true,
            Groups = new VerifiedWindowsGroupMembership(id, [first, second], now)
        };
        foreach (var own in new[] { first, second })
            Denied(() => SecurityActorGroupGuard.RequireOtherGroup(actor, own, now), "SECURITY_SELF_ACCESS_CHANGE_FORBIDDEN");
        SecurityActorGroupGuard.RequireOtherGroup(actor, other, now);
        Denied(() => SecurityActorGroupGuard.RequireOtherGroup(
            new SecurityActorContext(id, "DOMAIN\\admin", null), other, now), "SECURITY_GROUP_MEMBERSHIP_REQUIRED");
        foreach (var invalid in new[]
        {
            actor with { Groups = null },
            actor with { UserId = Guid.NewGuid() },
            actor with { Groups = new VerifiedWindowsGroupMembership(id, [], now.AddMinutes(-2)) },
            actor with { Groups = new VerifiedWindowsGroupMembership(id, [], now.AddSeconds(1)) }
        }) Denied(() => SecurityActorGroupGuard.RequireOtherGroup(invalid, other, now), "SECURITY_GROUP_MEMBERSHIP_REQUIRED");

        // A CI identity has no Windows group authority. Authoring a proposed
        // group assignment must not fabricate proof for runtime evaluation.
        var simulated = actor with { RequiresGroupProof = false, Groups = null };
        SecurityActorGroupGuard.RequireOtherGroup(simulated, first, now);
        if (SecurityActorGroupGuard.RequireUsableGroups(simulated, now).Count != 0 || simulated.Groups is not null)
            throw new InvalidOperationException("Test authoring created group authority.");
        Denied(() => SecurityActorGroupGuard.RequireOtherGroup(actor with
        {
            RequiresGroupProof = false,
            Groups = new VerifiedWindowsGroupMembership(Guid.NewGuid(), [], now)
        }, other, now), "SECURITY_GROUP_MEMBERSHIP_REQUIRED");
        Console.WriteLine("Own-group checks cover multiple groups, absent/stale/future/wrong-user proof and explicit test-only authoring.");
    }

    private static void Denied(Action action, string code)
    {
        try { action(); }
        catch (WellSiteAutoPilotException e) when (e.Code == code) { return; }
        throw new InvalidOperationException($"Expected group guard denial {code}.");
    }
}
