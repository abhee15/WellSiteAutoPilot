using Microsoft.AspNetCore.Authorization;
using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Server.Security;

public sealed record GrantPermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>
/// Fresh route prerequisites only. Resource reads still require scoped SQL;
/// writes retain their qualifying grants through the transaction commit.
/// </summary>
public sealed class GrantPermissionHandler(RequestAccessContext access, IRequestAccessRepository repository)
    : AuthorizationHandler<GrantPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        GrantPermissionRequirement requirement)
    {
        if (!access.TryGetSubject(context.User, out var subject)) return;
        var definition = ProductPermissionCatalog.Definitions
            .SingleOrDefault(item => item.Key == requirement.Permission);
        if (definition is null) return;

        var cancellation = context.Resource is HttpContext http ? http.RequestAborted : CancellationToken.None;
        var allowed = requirement.Permission == WellSitePermissions.SecurityManage
            ? await repository.HasAdministratorAsync(subject, cancellation)
            : definition.ScopeKind == "DeploymentWide"
                ? await repository.HasDeploymentPermissionAsync(subject, requirement.Permission, cancellation)
                : (await repository.GetPermissionHintsAsync(subject, cancellation))
                    .Contains(requirement.Permission, StringComparer.Ordinal);
        if (allowed) context.Succeed(requirement);
    }
}
