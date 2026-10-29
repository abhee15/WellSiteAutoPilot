using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;

internal static class WindowsGroupGrantChecks
{
    public static void Run()
    {
        var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        var user = Guid.NewGuid();
        var assetA = Guid.NewGuid();
        var assetB = Guid.NewGuid();
        IReadOnlySet<Guid> assets = new HashSet<Guid> { assetA, assetB };
        const string groupA = "S-1-5-21-101";
        const string groupB = "S-1-5-21-102";
        var engineer = new RoleDefinition(Guid.NewGuid(), "custom-engineer", "Custom Engineer", "",
            false, true, 1, now, now,
            [WellSitePermissions.AssetsRead, WellSitePermissions.AssetsManage, WellSitePermissions.LogicRead]);
        var reader = engineer with { Id = Guid.NewGuid(), Key = "custom-reader", Name = "Custom Reader",
            Permissions = [WellSitePermissions.AssetsRead] };
        var engineerGrant = new AccessGrant(Guid.NewGuid(), AccessGrantSubjectKind.Group,
            null, groupA, engineer.Id, AccessGrantScopeKind.ExactAssets, [assetA], true, 1);
        var readerGrant = engineerGrant with { Id = Guid.NewGuid(), GroupId = groupB,
            RoleId = reader.Id, AssetIds = [assetB] };
        var global = engineerGrant with { Id = Guid.NewGuid(), ScopeKind = AccessGrantScopeKind.DeploymentWide,
            AssetIds = [] };
        var grants = new[] { engineerGrant, readerGrant, global };
        var roles = new[] { engineer, reader };
        var proof = new VerifiedWindowsGroupMembership(user, [groupA, groupB], now);
        var manageA = Check(WellSitePermissions.AssetsManage, assetA, proof, now);
        var readB = Check(WellSitePermissions.AssetsRead, assetB, proof, now);
        if (!manageA.Allowed || manageA.GrantedByAssignmentId != engineerGrant.Id ||
            manageA.GrantedBySubjectKind != AccessGrantSubjectKind.Group ||
            !readB.Allowed || readB.GrantedByAssignmentId != readerGrant.Id ||
            Check(WellSitePermissions.AssetsManage, assetB, proof, now).Allowed ||
            !Check(WellSitePermissions.LogicRead, null, proof, now).Allowed)
            throw new InvalidOperationException("Multiple group grants lost scope, permission or provenance.");

        foreach (var (membership, at) in new (VerifiedWindowsGroupMembership?, DateTimeOffset?)[]
        {
            (null, now), (proof, null), (proof, now.AddSeconds(-1)),
            (proof, now.Add(VerifiedWindowsGroupMembership.MaximumAge).AddTicks(1)),
            (new(Guid.NewGuid(), [groupA, groupB], now), now),
            (new(user, [], now), now),
            (new(user, [groupB], now), now)
        })
            if (Check(WellSitePermissions.AssetsManage, assetA, membership, at).Allowed)
                throw new InvalidOperationException("Missing, expired, future or wrong-subject group proof authorized access.");

        // Membership in an unrelated group cannot broaden a direct grant.
        var direct = engineerGrant with { Id = Guid.NewGuid(), SubjectKind = AccessGrantSubjectKind.User,
            UserId = user, GroupId = null };
        var readerOnly = new VerifiedWindowsGroupMembership(user, [groupB], now);
        if (!ScopedGrantEvaluator.Authorize(user, true, WellSitePermissions.AssetsManage, assetA,
                assets, [direct, readerGrant], roles, readerOnly, now).Allowed ||
            ScopedGrantEvaluator.Authorize(user, true, WellSitePermissions.AssetsManage, assetB,
                assets, [direct, readerGrant], roles, readerOnly, now).Allowed)
            throw new InvalidOperationException("Direct and group grants cross-combined permission and scope.");
        foreach (var malformed in new[]
        {
            engineerGrant with { UserId = user },
            engineerGrant with { GroupId = "s-1-5-21-101" },
            engineerGrant with { IsActive = false },
            engineerGrant with { GroupId = "S-1-5-21-999" }
        })
            if (ScopedGrantEvaluator.Authorize(user, true, WellSitePermissions.AssetsManage, assetA,
                    assets, [malformed], roles, proof, now).Allowed)
                throw new InvalidOperationException("Malformed, revoked or non-member group grant authorized access.");
        if (ScopedGrantEvaluator.Authorize(user, false, WellSitePermissions.AssetsManage, assetA,
                assets, grants, roles, proof, now).Allowed)
            throw new InvalidOperationException("Group membership reactivated an inactive product user.");

        var mutable = new HashSet<string> { groupA };
        var frozen = new VerifiedWindowsGroupMembership(user, mutable, now);
        mutable.Add(groupB);
        if (frozen.GroupIds.Contains(groupB))
            throw new InvalidOperationException("Membership proof retained a mutable caller-owned set.");
        foreach (var invalid in new[] { new[] { "CORP\\Engineers" }, new[] { "S-1-5-021-1" },
            Enumerable.Repeat(groupA, VerifiedWindowsGroupMembership.MaximumGroups + 1).ToArray() })
        {
            try
            {
                _ = new VerifiedWindowsGroupMembership(user, invalid, now);
                throw new InvalidOperationException("Malformed/unbounded group proof was accepted.");
            }
            catch (WellSiteAutoPilotException error) when (error.Code == "SECURITY_GROUP_PROOF_INVALID") { }
        }
        Console.WriteLine("Direct/multiple-group grant isolation, proof binding, freshness and provenance passed.");

        EffectiveAccessDecision Check(string permission, Guid? asset,
            VerifiedWindowsGroupMembership? membership, DateTimeOffset? at) =>
            ScopedGrantEvaluator.Authorize(user, true, permission, asset, assets, grants, roles, membership, at);
    }
}
