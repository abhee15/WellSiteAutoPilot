using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Server.Security;

internal static class RequestAccessContextChecks
{
    public static void Run()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var profile = new UserAccessProfile(userId, "NAME:CORP\\USER", "CORP\\User", "CORP\\USER", null,
            true, now, now, [], []);
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.Name, profile.IdentityName),
                // Spoofed permission, role and Asset claims have no bearing on
                // the subject/proof produced by this host context.
                new Claim(SecurityClaimTypes.Permission, WellSitePermissions.SecurityManage),
                new Claim(SecurityClaimTypes.Role, "Admin"), new Claim(SecurityClaimTypes.AssetScope, "*")
            ], "test-principal"))
        };
        var membership = new VerifiedWindowsGroupMembership(userId, ["S-1-5-21-1201", "S-1-5-21-1202"], now);
        var groups = new CountingGroups(membership);
        var access = new RequestAccessContext(new ConfiguredPrincipalResolver("Negotiate"), groups);
        ExpectDenied(() => _ = access.Subject);
        access.Initialize(http.User, profile);
        var first = access.CreateActor(http);
        var second = access.CreateActor(http);
        if (access.Subject.UserId != userId || !ReferenceEquals(access.Subject.Groups, membership) ||
            !ReferenceEquals(first.Groups, second.Groups) || !first.RequiresGroupProof || groups.Calls != 1)
            throw new InvalidOperationException("Request context re-resolved or lost its bound membership proof.");
        ExpectDenied(() => access.Initialize(http.User, profile));
        ExpectDenied(() => access.CreateActor(new DefaultHttpContext { User = new ClaimsPrincipal(http.User) }));

        var testGroups = new CountingGroups(membership);
        var testAccess = new RequestAccessContext(new ConfiguredPrincipalResolver("TestHeader"), testGroups);
        testAccess.Initialize(http.User, profile);
        if (testAccess.Subject.Groups is not null || testAccess.CreateActor(http).RequiresGroupProof || testGroups.Calls != 0)
            throw new InvalidOperationException("TestHeader acquired Windows-group authority.");
        ExpectDenied(() => new RequestAccessContext(new ConfiguredPrincipalResolver("Negotiate"), groups)
            .Initialize(http.User, profile with { IsActive = false }));
        ExpectDenied(() => new RequestAccessContext(new ConfiguredPrincipalResolver("Negotiate"), groups)
            .Initialize(http.User, profile with { IdentityKey = "NAME:CORP\\OTHER" }));
        ExpectDenied(() => new RequestAccessContext(new ConfiguredPrincipalResolver("Negotiate"),
            new CountingGroups(new VerifiedWindowsGroupMembership(Guid.NewGuid(), ["S-1-5-21-1201"], now)))
            .Initialize(http.User, profile));
        var direct = new RequestAccessContext(new ConfiguredPrincipalResolver("Negotiate"), new CountingGroups(null));
        direct.Initialize(http.User, profile);
        if (direct.Subject.Groups is not null || !direct.CreateActor(http).RequiresGroupProof)
            throw new InvalidOperationException("Missing Windows proof was represented as a verified empty token.");
        var identityGroups = new CountingGroups(membership);
        var identityAccess = new RequestAccessContext(new ConfiguredPrincipalResolver("Negotiate"), identityGroups);
        identityAccess.Initialize(http.User, new UserIdentity(userId, profile.IdentityKey, profile.IdentityName, null, true, now, now));
        if (identityAccess.Subject.UserId != userId || !ReferenceEquals(identityAccess.Subject.Groups, membership) || identityGroups.Calls != 1)
            throw new InvalidOperationException("Identity-only request binding lost its trusted subject observation.");
        var opaque = new VerifiedGroupMembership(userId, ["CaseSensitiveGroup"], now, "tenant-a");
        var otherProvider = new RequestAccessContext(new OtherProvider(), new NeutralGroups(opaque));
        otherProvider.Initialize(http.User, new UserIdentity(userId, "OIDC:tenant-a/user-1", "person", null, true, now, now));
        if (!ReferenceEquals(otherProvider.Subject.Groups, opaque))
            throw new InvalidOperationException("A non-Windows trusted host adapter lost its group evidence.");
        Console.WriteLine("Request identity binding, one observation, TestHeader separation, missing/mismatched proof and inactive/foreign user refusal passed.");
    }

    private static void ExpectDenied(Action action)
    {
        try { action(); throw new InvalidOperationException("Invalid request context was accepted."); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == FailureKind.Authorization) { }
    }

    private sealed class OtherProvider : IAuthenticatedPrincipalResolver
    {
        public AuthenticatedExternalIdentity Resolve(ClaimsPrincipal authenticatedPrincipal) =>
            new("oidc-test", "user-1", "OIDC:tenant-a/user-1", "person", null);
    }
    private sealed class NeutralGroups(VerifiedGroupMembership membership) : IIdentityGroupResolver
    {
        public VerifiedGroupMembership? Resolve(ClaimsPrincipal principal, AuthenticatedExternalIdentity identity, Guid localUserId) => membership;
    }

    private sealed class CountingGroups(VerifiedWindowsGroupMembership? membership) : IIdentityGroupResolver
    {
        public int Calls { get; private set; }
        public VerifiedGroupMembership? Resolve(ClaimsPrincipal principal, AuthenticatedExternalIdentity identity, Guid localUserId)
        {
            Calls++;
            return membership;
        }
    }
}
