using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;

internal static class DirectoryNamespaceChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var user = Guid.NewGuid(); var asset = Guid.NewGuid();
        var role = new RoleDefinition(Guid.NewGuid(), "engineer", "Engineer", "", false, true, 1, now, now,
            [WellSitePermissions.AssetsRead]);
        var grant = new AccessGrant(Guid.NewGuid(), AccessGrantSubjectKind.Group, null, "CaseSensitiveId", role.Id,
            AccessGrantScopeKind.ExactAssets, [asset], true, 1) { DirectoryId = "tenant-a" };
        bool Allowed(string directory, string id) => ScopedGrantEvaluator.Authorize(user, true,
            WellSitePermissions.AssetsRead, asset, new HashSet<Guid> { asset }, [grant], [role],
            new VerifiedGroupMembership(user, [id], now, directory), now).Allowed;
        if (!Allowed("tenant-a", "CaseSensitiveId") || Allowed("tenant-b", "CaseSensitiveId") ||
            Allowed("tenant-a", "casesensitiveid") || Allowed("windows-ad", "CaseSensitiveId"))
            throw new InvalidOperationException("Directory namespace or opaque-ID case isolation failed.");
        var subject = AccessGrantSubject.ForGroup("tenant-a", "CaseSensitiveId");
        if (subject.DirectoryId != "tenant-a" || subject.GroupId != "CaseSensitiveId")
            throw new InvalidOperationException("Group subject was silently normalized.");
        var actor = new SecurityActorContext(user, "example", null)
        { Groups = new VerifiedGroupMembership(user, ["CaseSensitiveId"], now, "tenant-a") };
        SecurityActorGroupGuard.RequireOtherGroup(actor, "CaseSensitiveId", now, "tenant-b");
        Console.WriteLine("Directory-qualified group grants preserve namespace and opaque-ID case.");
    }
}
