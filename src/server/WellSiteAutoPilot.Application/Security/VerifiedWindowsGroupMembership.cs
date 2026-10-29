namespace WellSiteAutoPilot.Application.Security;

/// <summary>Windows adapter validation; the authorization engine consumes neutral evidence.</summary>
public sealed class VerifiedWindowsGroupMembership : VerifiedGroupMembership
{
    public VerifiedWindowsGroupMembership(Guid userId, IEnumerable<string> enabledGroupIds, DateTimeOffset observedAtUtc)
        : base(userId, Validate(enabledGroupIds), observedAtUtc, "windows-ad") { }

    private static IEnumerable<string> Validate(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        foreach (var id in ids)
        {
            if (!WindowsSidFormat.IsValid(id))
                throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException("SECURITY_GROUP_PROOF_INVALID",
                    WellSiteAutoPilot.Failures.FailureKind.Authorization, "Windows group membership is invalid.");
            yield return id.ToUpperInvariant();
        }
    }
}
