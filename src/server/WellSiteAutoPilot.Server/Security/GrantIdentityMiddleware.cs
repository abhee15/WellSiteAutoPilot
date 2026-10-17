using Microsoft.Extensions.Options;
using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Server.Security;

/// <summary>
/// Identity-only request initialization for the complete grant runtime cutover.
/// Provisioning never grants authority; fresh-deployment bootstrap is explicit.
/// </summary>
public sealed class GrantIdentityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserIdentityService identities,
        IAuthenticatedPrincipalResolver principals, RequestAccessContext access,
        IGrantBootstrapRepository bootstrap, IOptions<SecurityOptions> options, TimeProvider clock)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var external = principals.Resolve(context.User);
            var identity = await identities.ResolveAuthenticatedAsync(external.PersistedIdentityKey,
                external.IdentityName, external.DisplayName, context.RequestAborted);
            if (!identity.IsActive)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            access.Initialize(context.User, identity);
            var normalizedName = UserAccessService.NormalizeIdentity(external.IdentityName);
            if (options.Value.BootstrapAdministrators.Where(item => !string.IsNullOrWhiteSpace(item))
                .Any(item => UserAccessService.NormalizeIdentity(item) == normalizedName))
                await bootstrap.TryInitializeAdministratorAsync(identity.Id, clock.GetUtcNow(), context.RequestAborted);
        }

        await next(context);
    }
}
