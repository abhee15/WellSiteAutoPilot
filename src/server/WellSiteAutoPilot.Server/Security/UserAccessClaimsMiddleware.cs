using System.Security.Claims;
using Microsoft.Extensions.Options;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;

namespace WellSiteAutoPilot.Server.Security;

public sealed class UserAccessClaimsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        UserAccessService userAccessService,
        IAuthenticatedPrincipalResolver principalResolver,
        IOptions<SecurityOptions> options)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            // The host's configured provider resolves the trusted external
            // identity. Access provisioning no longer interprets Windows SIDs
            // or external identity-provider claim shapes.
            var externalIdentity = principalResolver.Resolve(context.User);

            var bootstrapAdmins = options.Value.BootstrapAdministrators
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(UserAccessService.NormalizeIdentity)
                .ToHashSet(StringComparer.Ordinal);

            var profile = await userAccessService.ResolveAuthenticatedAsync(
                externalIdentity.PersistedIdentityKey,
                externalIdentity.IdentityName,
                externalIdentity.DisplayName,
                bootstrapAdmins,
                context.RequestAborted);

            if (!profile.IsActive)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var claims = new List<Claim>
            {
                new(SecurityClaimTypes.UserId, profile.Id.ToString())
            };

            claims.AddRange(
                profile.Roles.Select(
                    role => new Claim(
                        SecurityClaimTypes.Role,
                        role.ToString())));

            claims.AddRange(
                WellSitePermissions.All
                    .Where(
                        permission =>
                            RolePermissionMap.HasPermission(
                                profile.Roles,
                                permission))
                    .Select(
                        permission =>
                            new Claim(
                                SecurityClaimTypes.Permission,
                                permission)));

            if (profile.IsAdministrator)
            {
                claims.Add(
                    new Claim(
                        SecurityClaimTypes.AssetScope,
                        "*"));
            }
            else
            {
                claims.AddRange(
                    profile.AssetScopeIds.Select(
                        assetId =>
                            new Claim(
                                SecurityClaimTypes.AssetScope,
                                assetId.ToString())));
            }

            context.User.AddIdentity(
                new ClaimsIdentity(
                    claims,
                    "WellSiteAutoPilot.Access"));
        }

        await next(context);
    }
}
