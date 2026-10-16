using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Server.Security;

internal static class WindowsGroupResolverChecks
{
    public static void Run()
    {
        var user = Guid.NewGuid();
        var external = new AuthenticatedExternalIdentity("windows", "S-1-5-21-777",
            "SID:S-1-5-21-777", "CORP\\User", null);
        var forged = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "CORP\\User"),
             new Claim(ClaimTypes.PrimarySid, external.SubjectId),
             new Claim(ClaimTypes.GroupSid, "S-1-5-32-544")], "Negotiate"));
        var resolver = new WindowsTokenGroupResolver("Negotiate", TimeProvider.System);
        if (resolver.Resolve(forged, external, user) is not null ||
            new WindowsTokenGroupResolver("TestHeader", TimeProvider.System)
                .Resolve(forged, external, user) is not null)
            throw new InvalidOperationException("Generic/CI group claims became trusted Windows token membership.");

        if (OperatingSystem.IsWindows())
            RunWindows(resolver, external, user, forged);
        Console.WriteLine("Group resolver rejects generic, header and secondary-identity claims.");
    }

    [SupportedOSPlatform("windows")]
    private static void RunWindows(WindowsTokenGroupResolver resolver,
        AuthenticatedExternalIdentity external, Guid user, ClaimsPrincipal forged)
    {
            using var identity = WindowsIdentity.GetCurrent();
            var nativeSid = identity.User!.Value;
            var nativeExternal = external with { SubjectId = nativeSid, PersistedIdentityKey = $"SID:{nativeSid}" };
            var principal = new WindowsPrincipal(identity);
            var proof = resolver.Resolve(principal, nativeExternal, user) ??
                throw new InvalidOperationException("Native Windows token did not produce membership proof.");
            var expected = identity.Groups!.OfType<SecurityIdentifier>()
                .Where(principal.IsInRole).Select(group => group.Value.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
            if (proof.UserId != user || !proof.GroupSids.SetEquals(expected))
                throw new InvalidOperationException("Membership included disabled/deny-only groups or lost enabled token groups.");
            // An actual token appended as a secondary identity is not the caller.
            forged.AddIdentity(identity);
            if (resolver.Resolve(forged, nativeExternal, user) is not null ||
                new ConfiguredPrincipalResolver("Negotiate").Resolve(forged).SubjectId != external.SubjectId)
                throw new InvalidOperationException("Secondary Windows identity supplied the caller's identity/groups.");
            Console.WriteLine("Native Windows enabled-token group resolution passed (not a browser/AD handshake test).");
    }

}
