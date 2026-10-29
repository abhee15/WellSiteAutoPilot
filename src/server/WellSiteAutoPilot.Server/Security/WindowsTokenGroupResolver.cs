using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Server.Security;

public interface IIdentityGroupResolver
{
    VerifiedGroupMembership? Resolve(ClaimsPrincipal principal,
        AuthenticatedExternalIdentity externalIdentity, Guid localUserId);
}

/// <summary>
/// Reads enabled groups from the authenticated primary Windows access token.
/// Generic claims, secondary identities and CI headers are never membership
/// proof. No directory lookup, cross-request cache or user impersonation.
/// </summary>
public sealed class WindowsTokenGroupResolver(string authenticationMode, TimeProvider clock)
    : IIdentityGroupResolver
{
    public VerifiedGroupMembership? Resolve(ClaimsPrincipal principal,
        AuthenticatedExternalIdentity externalIdentity, Guid localUserId)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(externalIdentity);
        if (!string.Equals(authenticationMode, SecurityOptions.NegotiateAuthenticationMode,
                StringComparison.OrdinalIgnoreCase) || externalIdentity.ProviderId != "windows" ||
            !OperatingSystem.IsWindows() || principal.Identities.FirstOrDefault() is not WindowsIdentity identity ||
            !identity.IsAuthenticated)
            return null;

        return ResolveWindows(identity, externalIdentity, localUserId);
    }

    [SupportedOSPlatform("windows")]
    private VerifiedWindowsGroupMembership? ResolveWindows(WindowsIdentity identity,
        AuthenticatedExternalIdentity externalIdentity, Guid localUserId)
    {
        var sid = identity.User?.Value;
        if (sid is null || !string.Equals(externalIdentity.PersistedIdentityKey,
                $"SID:{sid}", StringComparison.OrdinalIgnoreCase))
            throw new WellSiteAutoPilotException("SECURITY_GROUP_SUBJECT_MISMATCH", FailureKind.Authorization,
                "The group token does not match the resolved user identity.");
        var groups = identity.Groups;
        if (groups is null) return null;
        if (groups.Count > VerifiedWindowsGroupMembership.MaximumGroups)
            throw new WellSiteAutoPilotException("SECURITY_GROUP_PROOF_INVALID", FailureKind.Authorization,
                "The Windows token exceeds the supported group membership bound.");

        var token = new WindowsPrincipal(identity);
        // Groups can include deny-only/disabled SIDs. IsInRole(SID) checks
        // enabled token membership; copying every Groups entry could elevate
        // a filtered/UAC token into an administrative group grant.
        var enabled = groups.OfType<SecurityIdentifier>()
            .Where(token.IsInRole).Select(group => group.Value).ToArray();
        return new VerifiedWindowsGroupMembership(localUserId, enabled, clock.GetUtcNow());
    }
}
