using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>Own-access protection, not a source of permission or membership.</summary>
public static class SecurityActorGroupGuard
{
    public static IReadOnlySet<string> RequireUsableGroups(SecurityActorContext actor, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Groups is { } membership)
        {
            if (actor.UserId is not { } userId || !membership.IsUsableFor(userId, nowUtc))
                throw Required();
            return membership.GroupIds;
        }
        if (actor.RequiresGroupProof) throw Required();
        // This caller has no Windows-group authority (e.g. CI TestHeader).
        // Do not manufacture a membership proof or use HTTP group claims.
        return new HashSet<string>(StringComparer.Ordinal);
    }

    public static void RequireOtherGroup(SecurityActorContext actor, string groupId, DateTimeOffset nowUtc, string directoryId = "windows-ad")
    {
        if (RequireUsableGroups(actor, nowUtc).Contains(groupId) && actor.Groups?.DirectoryId == directoryId)
            throw new WellSiteAutoPilotException("SECURITY_SELF_ACCESS_CHANGE_FORBIDDEN",
                FailureKind.Authorization, "Administrators may not change assignments for their own groups.");
    }

    private static WellSiteAutoPilotException Required() => new(
        "SECURITY_GROUP_MEMBERSHIP_REQUIRED", FailureKind.Authorization,
        "Fresh verified group membership is required before changing group-derived access.");
}
