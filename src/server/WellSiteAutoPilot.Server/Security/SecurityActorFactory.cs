using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Http;

namespace WellSiteAutoPilot.Server.Security;

/// <summary>Bind administration changes to the authenticated provider's token.</summary>
public sealed class SecurityActorFactory(
    IAuthenticatedPrincipalResolver principals, IIdentityGroupResolver groups)
{
    public SecurityActorContext Create(HttpContext context, Guid localUserId)
    {
        var external = principals.Resolve(context.User);
        return new SecurityActorContext(localUserId, external.IdentityName,
            CorrelationContext.GetCorrelationId(context))
        {
            WindowsGroups = groups.Resolve(context.User, external, localUserId),
            RequiresWindowsGroupProof = external.ProviderId == "windows"
        };
    }
}
