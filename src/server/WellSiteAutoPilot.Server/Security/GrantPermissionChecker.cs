using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Server.Security;

/// <summary>
/// The one place that decides whether a bound subject currently holds a route
/// permission: protected permissions need an administrator, deployment-wide
/// ones a deployment grant, resource-scoped ones any qualifying grant hint.
/// Shared by the REST policy handler and the live-update hub so both ask the
/// repository the same question. Unknown permissions are never held.
/// </summary>
public static class GrantPermissionChecker
{
    public static async Task<IReadOnlySet<string>> HeldAsync(IRequestAccessRepository repository,
        AccessSubject subject, IEnumerable<string> permissions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(permissions);

        var held = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyCollection<string>? hints = null;
        foreach (var permission in permissions.Distinct(StringComparer.Ordinal))
        {
            var definition = ProductPermissionCatalog.Definitions.SingleOrDefault(item => item.Key == permission);
            if (definition is null) continue;

            bool allowed;
            if (permission == WellSitePermissions.SecurityManage)
                allowed = await repository.HasAdministratorAsync(subject, cancellationToken);
            else if (definition.ScopeKind == "DeploymentWide")
                allowed = await repository.HasDeploymentPermissionAsync(subject, permission, cancellationToken);
            else
            {
                hints ??= await repository.GetPermissionHintsAsync(subject, cancellationToken);
                allowed = hints.Contains(permission, StringComparer.Ordinal);
            }

            if (allowed) held.Add(permission);
        }

        return held;
    }
}
