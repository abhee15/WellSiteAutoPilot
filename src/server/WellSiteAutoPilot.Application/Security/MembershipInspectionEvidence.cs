namespace WellSiteAutoPilot.Application.Security;

/// <summary>
/// Safe, read-only description of the request's membership-evidence provenance.
/// This is NOT an authorization result or a claim about current directory contents.
/// </summary>
public enum MembershipInspectionState
{
    /// <summary>The requested user is not the principal bound to this request.</summary>
    NotEvaluated,
    /// <summary>The host supplied no trusted membership evidence for the signed-in user.</summary>
    Unavailable,
    /// <summary>Previously observed request evidence is not usable at the point of inspection.</summary>
    Stale,
    /// <summary>A usable snapshot is bound to this authenticated request's user, not a live directory query.</summary>
    BoundRequestSnapshot
}

/// <summary>
/// Metadata safe for a future privileged inspector. Never includes group IDs or
/// credentials; it must not be interpreted as proof of absent access.
/// </summary>
public sealed record MembershipInspectionEvidence(
    MembershipInspectionState State,
    string? DirectoryId = null,
    DateTimeOffset? ObservedAtUtc = null)
{
    /// <summary>
    /// Evaluate only server-owned request evidence. This does not fetch arbitrary
    /// user groups or permit a caller to borrow an administrator's groups for
    /// the person under inspection.
    /// </summary>
    public static MembershipInspectionEvidence ForSelectedUser(
        AccessSubject boundPrincipal, Guid selectedUserId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(boundPrincipal);
        if (selectedUserId == Guid.Empty)
            throw new ArgumentException("A selected local user ID is required.", nameof(selectedUserId));

        // Never use the administrator's token groups when inspecting another user.
        if (selectedUserId != boundPrincipal.UserId)
            return new(MembershipInspectionState.NotEvaluated);

        if (boundPrincipal.Groups is not { } proof)
            return new(MembershipInspectionState.Unavailable);

        if (!proof.IsUsableFor(selectedUserId, nowUtc))
            return new(MembershipInspectionState.Stale);

        // This observed timestamp bounds proof reuse only. It is not the Windows
        // token issue time, an AD check timestamp, or a guaranteed revocation SLA.
        return new(MembershipInspectionState.BoundRequestSnapshot,
            proof.DirectoryId, proof.ObservedAtUtc);
    }
}
