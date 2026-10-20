using Microsoft.AspNetCore.Authorization;
using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Server.Security;

public sealed record GrantPermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Satisfied by holding any one of the listed permissions, for a page several kinds of administrator use.</summary>
public sealed record GrantAnyPermissionRequirement(IReadOnlyList<string> Permissions) : IAuthorizationRequirement;

public sealed class GrantAnyPermissionHandler(RequestAccessContext access, IRequestAccessRepository repository)
    : AuthorizationHandler<GrantAnyPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        GrantAnyPermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        if (!access.TryGetSubject(context.User, out var subject)) return;
        var cancellation = context.Resource is HttpContext http ? http.RequestAborted : CancellationToken.None;
        var held = await GrantPermissionChecker.HeldAsync(repository, subject, requirement.Permissions, cancellation);
        if (held.Count > 0) context.Succeed(requirement);
    }
}

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
        var cancellation = context.Resource is HttpContext http ? http.RequestAborted : CancellationToken.None;
        var held = await GrantPermissionChecker.HeldAsync(repository, subject, [requirement.Permission], cancellation);
        if (held.Contains(requirement.Permission)) context.Succeed(requirement);
    }
}
