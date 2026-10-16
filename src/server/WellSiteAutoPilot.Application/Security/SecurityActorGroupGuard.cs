using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>Own-access protection, not a source of permission or membership.</summary>
public static class SecurityActorGroupGuard
{
    public static IReadOnlySet<string> RequireUsableGroups(SecurityActorContext actor, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.WindowsGroups is { } membership)
        {
            if (actor.UserId is not { } userId || !membership.IsUsableFor(userId, nowUtc))
                throw Required();
            return membership.GroupSids;
        }
        if (actor.RequiresWindowsGroupProof) throw Required();
        // This caller has no Windows-group authority (e.g. CI TestHeader).
        // Do not manufacture a membership proof or use HTTP group claims.
        return new HashSet<string>(StringComparer.Ordinal);
    }

    public static void RequireOtherGroup(SecurityActorContext actor, string groupSid, DateTimeOffset nowUtc)
    {
        if (RequireUsableGroups(actor, nowUtc).Contains(groupSid))
            throw new WellSiteAutoPilotException("SECURITY_SELF_ACCESS_CHANGE_FORBIDDEN",
                FailureKind.Authorization, "Administrators may not change assignments for their own Windows groups.");
    }

    private static WellSiteAutoPilotException Required() => new(
        "SECURITY_GROUP_MEMBERSHIP_REQUIRED", FailureKind.Authorization,
        "Fresh verified Windows group membership is required before changing group-derived access.");
}
