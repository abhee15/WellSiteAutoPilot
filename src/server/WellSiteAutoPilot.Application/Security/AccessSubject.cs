namespace WellSiteAutoPilot.Application.Security;

/// <summary>
/// Request-bound subject supplied by the authenticated host. This carries no
/// role, permission or Asset union; storage resolves each complete grant.
/// Never construct Windows membership from a request DTO or header.
/// </summary>
public sealed class AccessSubject
{
    public Guid UserId { get; }
    public VerifiedGroupMembership? Groups { get; }

    public AccessSubject(Guid userId, VerifiedGroupMembership? windowsGroups = null)
    {
        if (userId == Guid.Empty) throw new ArgumentException("A persisted user is required.", nameof(userId));
        if (windowsGroups is not null && windowsGroups.UserId != userId)
            throw new ArgumentException("Group proof belongs to another user.", nameof(windowsGroups));
        UserId = userId;
        Groups = windowsGroups;
    }
}
