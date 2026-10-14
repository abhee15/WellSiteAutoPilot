using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

public sealed record RolePermissionDefinition(
    string Key,
    string Category,
    string ScopeKind,
    bool IsPrivileged);

public sealed record RoleMutationCommand(
    string Name,
    string Description,
    IReadOnlyCollection<string> Permissions,
    bool IsActive = true);

/// <summary>
/// Product-defined capabilities, independent of customer role composition.
/// Unrecognized strings must never become effective application permissions.
/// </summary>
public static class ProductPermissionCatalog
{
    public static IReadOnlyCollection<RolePermissionDefinition> Definitions { get; } =
    [
        new(WellSitePermissions.AssetsRead, "Assets", "ResourceScoped", false),
        new(WellSitePermissions.AssetsManage, "Assets", "ResourceScoped", false),
        new(WellSitePermissions.LogicRead, "Logic", "DeploymentWide", false),
        new(WellSitePermissions.LogicManage, "Logic", "DeploymentWide", false),
        new(WellSitePermissions.ConfiguredLogicRead, "ConfiguredLogic", "ResourceScoped", false),
        new(WellSitePermissions.ConfiguredLogicManage, "ConfiguredLogic", "ResourceScoped", false),
        new(WellSitePermissions.ExecutionsRead, "Executions", "ResourceScoped", false),
        new(WellSitePermissions.ExecutionsRequest, "Executions", "ResourceScoped", false),
        new(WellSitePermissions.RecommendationsRead, "Recommendations", "ResourceScoped", false),
        new(WellSitePermissions.RecommendationsDecide, "Recommendations", "ResourceScoped", false),
        new(WellSitePermissions.SecurityManage, "Security", "DeploymentWide", true)
    ];

    private static readonly HashSet<string> Known =
        Definitions.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);

    public static bool IsKnown(string permission) =>
        Known.Contains(permission);
}

/// <summary>
/// Admin-only role authoring. Assigning effective user/AD group grants requires
/// the later scoped-entitlement evaluator and is deliberately not exposed here.
/// </summary>
public sealed class RoleCatalogService(IRoleCatalogRepository repository, TimeProvider timeProvider)
{
    public Task<IReadOnlyCollection<RoleDefinition>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed, FailureKind.Validation,
                "Role list limit must be between 1 and 500.");
        }

        return repository.ListAsync(limit, cancellationToken);
    }

    public async Task<RoleDefinition> GetRequiredAsync(
        Guid roleId,
        CancellationToken cancellationToken = default) =>
        await repository.GetAsync(roleId, cancellationToken) ??
        throw new WellSiteAutoPilotException(
            "SECURITY_ROLE_NOT_FOUND", FailureKind.NotFound,
            "The role was not found.");

    public Task<RoleDefinition> CreateAsync(
        RoleMutationCommand command,
        SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        RequireActor(actor);
        var normalized = NormalizeCommand(command);
        var now = timeProvider.GetUtcNow();
        return repository.CreateAsync(
            new RoleDefinition(
                Guid.NewGuid(),
                $"custom-{Guid.NewGuid():N}",
                normalized.Name,
                normalized.Description,
                false,
                normalized.IsActive,
                1,
                now,
                now,
                normalized.Permissions),
            actor, cancellationToken);
    }

    public async Task<RoleDefinition> UpdateAsync(
        Guid roleId,
        long expectedRevision,
        RoleMutationCommand command,
        SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        RequireActor(actor);
        if (expectedRevision < 1)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed, FailureKind.Validation,
                "Expected role revision must be positive.");
        }

        var normalized = NormalizeCommand(command);
        return await repository.UpdateAsync(
            roleId, expectedRevision, normalized, actor,
            timeProvider.GetUtcNow(), cancellationToken);
    }

    private static void RequireActor(SecurityActorContext actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.UserId is null || string.IsNullOrWhiteSpace(actor.IdentityName))
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_ACTOR_REQUIRED", FailureKind.Authorization,
                "An authenticated security administrator is required.");
        }
    }

    private static RoleMutationCommand NormalizeCommand(RoleMutationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Permissions);

        var name = command.Name?.Trim() ?? string.Empty;
        if (name.Length is < 2 or > 80 ||
            name.Any(char.IsControl) ||
            string.Equals(name, "Admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Engineer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Operator", StringComparison.OrdinalIgnoreCase))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed, FailureKind.Validation,
                "Role name is invalid or reserved for a system role.");
        }

        var description = command.Description?.Trim() ?? string.Empty;
        if (description.Length > 500 || description.Any(char.IsControl))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed, FailureKind.Validation,
                "Role description must be 500 characters or fewer.");
        }

        var permissions = command.Permissions
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (permissions.Length > ProductPermissionCatalog.Definitions.Count ||
            permissions.Any(item => item is null || !ProductPermissionCatalog.IsKnown(item)))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed, FailureKind.Validation,
                "Role contains an unsupported product permission.");
        }

        return new RoleMutationCommand(name, description, permissions, command.IsActive);
    }
}
