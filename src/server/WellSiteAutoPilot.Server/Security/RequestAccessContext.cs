using System.Security.Claims;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Http;

namespace WellSiteAutoPilot.Server.Security;

/// <summary>
/// Scoped host context initialized once after authenticated identity persistence.
/// No permissions or Asset wildcard are copied from claims. Repositories check
/// current grants/user state and proof freshness at the point of use.
/// </summary>
public sealed class RequestAccessContext(IAuthenticatedPrincipalResolver principals, IIdentityGroupResolver groups)
{
    private AccessSubject? subject;
    private ClaimsPrincipal? principal;
    private AuthenticatedExternalIdentity? external;

    public AccessSubject Subject => subject ?? throw Denied();

    public void Initialize(ClaimsPrincipal authenticatedPrincipal, UserAccessProfile profile)
    {
        ArgumentNullException.ThrowIfNull(authenticatedPrincipal);
        ArgumentNullException.ThrowIfNull(profile);
        if (subject is not null || !profile.IsActive || profile.Id == Guid.Empty) throw Denied();
        var identity = principals.Resolve(authenticatedPrincipal);
        if (!string.Equals(identity.PersistedIdentityKey, profile.IdentityKey, StringComparison.Ordinal)) throw Denied();
        // TestHeader is simulated authentication and can never carry native
        // Windows-group authority, even if a resolver is accidentally supplied.
        var membership = identity.ProviderId == "windows"
            ? groups.Resolve(authenticatedPrincipal, identity, profile.Id) : null;
        if (membership is not null && membership.UserId != profile.Id)
            throw new WellSiteAutoPilotException("SECURITY_GROUP_SUBJECT_MISMATCH", FailureKind.Authorization,
                "The group token does not match the resolved user identity.");
        subject = new AccessSubject(profile.Id, membership);
        principal = authenticatedPrincipal;
        external = identity;
    }

    public SecurityActorContext CreateActor(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var current = Subject;
        if (!ReferenceEquals(principal, context.User) || external is null) throw Denied();
        return new SecurityActorContext(current.UserId, external.IdentityName, CorrelationContext.GetCorrelationId(context))
        {
            WindowsGroups = current.WindowsGroups,
            RequiresWindowsGroupProof = external.ProviderId == "windows"
        };
    }

    private static WellSiteAutoPilotException Denied() => new("SECURITY_REQUEST_SUBJECT_INVALID",
        FailureKind.Authorization, "An authenticated, persisted request identity is required.");
}
