using WellSiteAutoPilot.Domain.Security;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>
/// Pure fail-closed permission evaluator. All permissions must match an active
/// role AND a trusted subject AND Asset scope within the SAME grant.
/// The caller must resolve user activity and active Assets from authoritative
/// server-owned storage. Groups require explicit request-bound membership proof.
/// </summary>
public static class ScopedGrantEvaluator
{
    public const int MaximumExactAssetsPerGrant = 500;
    public const string DeniedInactiveUser = "SECURITY_USER_INACTIVE";
    public const string DeniedUnknownPermission = "SECURITY_PERMISSION_UNKNOWN";
    public const string DeniedInvalidScope = "SECURITY_SCOPE_DENIED";
    public const string Allowed = "SECURITY_PERMISSION_GRANTED";

    public static EffectiveAccessDecision Authorize(
        Guid userId,
        bool userActive,
        string permission,
        Guid? targetAssetId,
        IReadOnlySet<Guid> activeAssetIds,
        IReadOnlyCollection<AccessGrant> assignments,
        IReadOnlyCollection<RoleDefinition> roles,
        VerifiedGroupMembership? membership = null,
        DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(activeAssetIds);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(roles);

        if (userId == Guid.Empty || !userActive)
        {
            return new(false, DeniedInactiveUser);
        }

        var productPermission = ProductPermissionCatalog.Definitions
            .SingleOrDefault(item => string.Equals(
                item.Key, permission, StringComparison.Ordinal));
        if (productPermission is null)
        {
            return new(false, DeniedUnknownPermission);
        }

        var scoped = string.Equals(
            productPermission.ScopeKind, "ResourceScoped", StringComparison.Ordinal);

        if ((scoped && (targetAssetId is not { } validId ||
                        validId == Guid.Empty ||
                        !activeAssetIds.Contains(validId))) ||
            (!scoped && targetAssetId is not null))
        {
            return new(false, DeniedInvalidScope);
        }

        var byId = roles
            .Where(role => role.Id != Guid.Empty && role.IsActive && role.Revision >= 1)
            .GroupBy(role => role.Id)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var groupIds = membership is not null && nowUtc is { } observed &&
            membership.IsUsableFor(userId, observed) ? membership.GroupIds : null;
        foreach (var grant in assignments)
        {
            var subjectMatches = grant.SubjectKind switch
            {
                AccessGrantSubjectKind.User => grant.UserId == userId && grant.GroupId is null,
                AccessGrantSubjectKind.Group => grant.UserId is null &&
                    grant.DirectoryId == membership?.DirectoryId &&
                    grant.GroupId is { } sid && groupIds is not null &&
                    groupIds.Contains(sid),
                _ => false
            };
            if (!grant.IsActive || grant.Id == Guid.Empty || grant.Revision < 1 ||
                !subjectMatches ||
                !byId.TryGetValue(grant.RoleId, out var candidates) ||
                candidates.Length != 1)
            {
                continue;
            }

            var role = candidates[0];
            if (!role.Permissions.Contains(permission, StringComparer.Ordinal))
            {
                continue;
            }

            if (grant.ScopeKind == AccessGrantScopeKind.DeploymentWide)
            {
                // Deployment-wide (e.g. Logic Catalog) is not equivalent to
                // All Assets. Never turn a global Logic permission into an
                // Asset-management grant when the role contains both kinds.
                if (!scoped && grant.AssetIds.Count == 0)
                {
                    return new(true, Allowed, grant.Id, grant.SubjectKind);
                }

                continue;
            }

            if (grant.ScopeKind == AccessGrantScopeKind.AllAssets)
            {
                // This scope requires an explicitly privileged administrative
                // assignment; it is never inferred from a parent Asset.
                if (scoped && grant.AssetIds.Count == 0)
                {
                    return new(true, Allowed, grant.Id, grant.SubjectKind);
                }

                continue;
            }

            if (grant.ScopeKind != AccessGrantScopeKind.ExactAssets ||
                grant.AssetIds.Count is < 1 or > MaximumExactAssetsPerGrant ||
                grant.AssetIds.Distinct().Count() != grant.AssetIds.Count ||
                grant.AssetIds.Any(id => id == Guid.Empty) ||
                grant.AssetIds.Any(id => !activeAssetIds.Contains(id)) ||
                !scoped)
            {
                continue;
            }

            if (targetAssetId is { } assetId &&
                grant.AssetIds.Contains(assetId))
            {
                return new(true, Allowed, grant.Id, grant.SubjectKind);
            }
        }

        return new(false, DeniedInvalidScope);
    }
}
