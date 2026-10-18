using System.Security.Claims;
using WellSiteAutoPilot.Application.Changes;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Server.Security;

namespace WellSiteAutoPilot.Server.Changes;

public interface IChangeAreaAccess
{
    /// <summary>
    /// Areas whose read permission the principal holds right now. Only the authenticated
    /// principal is trusted; permissions are read from the grant store, never from claims.
    /// </summary>
    Task<IReadOnlySet<ChangeArea>> GetReadableAreasAsync(ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}

public sealed class ChangeAreaAccess(UserIdentityService identities, IAuthenticatedPrincipalResolver principals,
    IIdentityGroupResolver groups, IRequestAccessRepository repository) : IChangeAreaAccess
{
    public async Task<IReadOnlySet<ChangeArea>> GetReadableAreasAsync(ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var readable = new HashSet<ChangeArea>();
        if (principal.Identity?.IsAuthenticated != true) return readable;

        var external = principals.Resolve(principal);
        // Same call the request middleware makes; a null display name never overwrites one.
        var user = await identities.ResolveAuthenticatedAsync(external.PersistedIdentityKey, external.IdentityName,
            null, cancellationToken);
        if (!user.IsActive) return readable;

        var membership = external.ProviderId == "windows" ? groups.Resolve(principal, external, user.Id) : null;
        var subject = new AccessSubject(user.Id, membership);
        var held = await GrantPermissionChecker.HeldAsync(repository, subject,
            ChangeAreaNames.All.Select(ChangeSignalProtocol.ReadPermission), cancellationToken);
        foreach (var area in ChangeAreaNames.All)
            if (held.Contains(ChangeSignalProtocol.ReadPermission(area))) readable.Add(area);
        return readable;
    }
}
