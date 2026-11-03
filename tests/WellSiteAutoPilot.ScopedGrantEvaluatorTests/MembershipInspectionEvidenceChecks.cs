using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;

/// <summary>
/// Slice 3 feasibility probes. The current request's Windows/token evidence is
/// not proof of an arbitrary selected user's group membership or of live AD state.
/// </summary>
internal static class MembershipInspectionEvidenceChecks
{
    public static void Run()
    {
        var now = new DateTimeOffset(2026, 11, 3, 19, 0, 0, TimeSpan.Zero);
        var actorId = Guid.NewGuid();
        var inspectedId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        const string directory = "tenant-case-sensitive";
        const string group = "Engineers-Id";
        var proof = new VerifiedGroupMembership(actorId, [group], now, directory);
        var actor = new AccessSubject(actorId, proof);

        // A request-bound administrator token must NEVER be usable as membership
        // proof for another directory user, even if the administrator belongs
        // to the very same group granted on the target account.
        var other = MembershipInspectionEvidence.ForSelectedUser(actor, inspectedId, now);
        if (other.State != MembershipInspectionState.NotEvaluated ||
            other.DirectoryId is not null || other.ObservedAtUtc is not null ||
            other.EstablishesCurrentDirectoryAbsence)
            throw new InvalidOperationException("Inspector borrowed the administrator's groups for another user.");

        var absent = MembershipInspectionEvidence.ForSelectedUser(new AccessSubject(actorId), actorId, now);
        if (absent.State != MembershipInspectionState.Unavailable ||
            absent.EstablishesCurrentDirectoryAbsence)
            throw new InvalidOperationException("Missing membership was misreported as verified group absence.");

        var usable = MembershipInspectionEvidence.ForSelectedUser(actor, actorId, now);
        if (usable.State != MembershipInspectionState.BoundRequestSnapshot ||
            usable.DirectoryId != directory || usable.ObservedAtUtc != now ||
            usable.EstablishesCurrentDirectoryAbsence)
            throw new InvalidOperationException("Bound proof lost its provenance or implied a fresh directory query.");

        // The one-minute clock bounds request evidence reuse; it says NOTHING
        // about the freshness/renewal of the underlying Windows authentication token.
        var atLimit = MembershipInspectionEvidence.ForSelectedUser(
            actor, actorId, now.Add(VerifiedGroupMembership.MaximumAge));
        var expired = MembershipInspectionEvidence.ForSelectedUser(
            actor, actorId, now.Add(VerifiedGroupMembership.MaximumAge).AddTicks(1));
        var future = MembershipInspectionEvidence.ForSelectedUser(actor, actorId, now.AddTicks(-1));
        if (atLimit.State != MembershipInspectionState.BoundRequestSnapshot ||
            expired.State != MembershipInspectionState.Stale ||
            future.State != MembershipInspectionState.Stale ||
            expired.DirectoryId is not null || future.ObservedAtUtc is not null)
            throw new InvalidOperationException("Inspected evidence ignored its time limit or leaked stale metadata.");

        // The existing evaluator is the only authorization engine. A valid group
        // grant authorizes only the principal whose own trusted proof matches.
        // Without that proof the runtime correctly refuses the grant, but a
        // separate administrative inspector must still say NOT EVALUATED.
        var role = new RoleDefinition(Guid.NewGuid(), "probe-reader", "Probe reader", "",
            false, true, 1, now, now, [WellSitePermissions.AssetsRead]);
        var grant = new AccessGrant(Guid.NewGuid(), AccessGrantSubjectKind.Group,
            null, group, role.Id, AccessGrantScopeKind.ExactAssets, [assetId], true, 1)
        { DirectoryId = directory };
        var resources = new HashSet<Guid> { assetId };
        bool Allowed(Guid userId, VerifiedGroupMembership? evidence) =>
            ScopedGrantEvaluator.Authorize(userId, true, WellSitePermissions.AssetsRead, assetId,
                resources, [grant], [role], evidence, now).Allowed;
        if (!Allowed(actorId, proof) ||
            Allowed(inspectedId, null) ||
            Allowed(inspectedId, proof) ||
            Allowed(actorId, new VerifiedGroupMembership(actorId, [group], now, "other-tenant")) ||
            Allowed(actorId, new VerifiedGroupMembership(actorId, ["engineers-id"], now, directory)))
            throw new InvalidOperationException("Membership inspection probe broke subject or directory isolation.");

        // A valid direct grant remains independently usable with missing group
        // evidence, but that does not prove the user has *only* direct access.
        var directGrant = grant with
        {
            Id = Guid.NewGuid(),
            SubjectKind = AccessGrantSubjectKind.User,
            UserId = inspectedId,
            GroupId = null
        };
        if (!ScopedGrantEvaluator.Authorize(inspectedId, true,
            WellSitePermissions.AssetsRead, assetId, resources, [directGrant],
            [role]).Allowed ||
            MembershipInspectionEvidence.ForSelectedUser(actor, inspectedId, now).State
                != MembershipInspectionState.NotEvaluated)
            throw new InvalidOperationException("Direct grant or unknown group evidence was misclassified.");

        try
        {
            _ = MembershipInspectionEvidence.ForSelectedUser(actor, Guid.Empty, now);
            throw new InvalidOperationException("Empty inspection target accepted.");
        }
        catch (ArgumentException exception) when (exception.ParamName == "selectedUserId")
        {
        }

        Console.WriteLine("Inspection evidence keeps other-user, missing, stale and own-token proof distinct.");
    }
}
