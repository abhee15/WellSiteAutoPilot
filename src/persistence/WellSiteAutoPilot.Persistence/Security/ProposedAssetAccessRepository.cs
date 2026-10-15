using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Persistence.Assets;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>
/// SQL-backed proposed entitlements ONLY; does not control live authorization.
/// All result shapes (page, single-Asset, bulk) use the SAME indivisible
/// grant/role/Asset predicate so a list preview cannot differ from a decision.
/// Filter before pagination; never union a role with another grant's scope.
/// </summary>
public sealed class ProposedAssetAccessRepository(WellSiteAutoPilotDbContext db)
    : IProposedAssetAccessRepository
{
    public async Task<IReadOnlyCollection<ProposedAuthorizedAsset>> ListAsync(
        Guid userId, string permission, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        if (!ValidResourceQuery(userId, permission) ||
            offset < 0 || offset > 1_000_000 || limit is < 1 or > 500)
        {
            return [];
        }

        return await AuthorizedAssets(userId, permission)
            .OrderBy(asset => asset.Name).ThenBy(asset => asset.Id)
            .Skip(offset).Take(limit)
            .Select(asset => new ProposedAuthorizedAsset(asset.Id, asset.Name))
            .ToArrayAsync(cancellationToken);
    }

    public Task<bool> IsAssetAuthorizedAsync(
        Guid userId, string permission, Guid assetId,
        CancellationToken cancellationToken = default)
    {
        if (!ValidResourceQuery(userId, permission) || assetId == Guid.Empty)
        {
            return Task.FromResult(false);
        }

        return AuthorizedAssets(userId, permission)
            .AnyAsync(asset => asset.Id == assetId, cancellationToken);
    }

    public async Task<bool> AreAssetsAuthorizedAsync(
        Guid userId, string permission, IReadOnlyCollection<Guid> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetIds);

        // Verify the complete caller-supplied set; accepting only the first
        // page would allow an unauthorized Asset after the page boundary.
        if (!ValidResourceQuery(userId, permission) ||
            assetIds.Count is < 1 or > 500 ||
            assetIds.Any(id => id == Guid.Empty) ||
            assetIds.Distinct().Count() != assetIds.Count)
        {
            return false;
        }

        var ids = assetIds.ToArray();
        var authorizedCount = await AuthorizedAssets(userId, permission)
            .CountAsync(asset => ids.Contains(asset.Id), cancellationToken);
        return authorizedCount == ids.Length;
    }

    public Task<bool> IsDeploymentPermissionGrantedAsync(
        Guid userId, string permission,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty ||
            !ProductPermissionCatalog.Definitions.Any(item =>
                item.Key == permission && item.ScopeKind == "DeploymentWide"))
        {
            return Task.FromResult(false);
        }

        var activeUsers = db.Users.AsNoTracking();
        var roleDefinitions = db.RoleDefinitions.AsNoTracking();
        var rolePermissions = db.RolePermissions.AsNoTracking();
        var bindings = db.AccessGrantAssets.AsNoTracking();
        return db.AccessGrants.AsNoTracking().AnyAsync(grant =>
            grant.Id != Guid.Empty && grant.RoleId != Guid.Empty &&
            grant.SubjectKind == "User" && grant.UserId == userId &&
            grant.GroupSid == null && grant.IsActive && grant.Revision >= 1 &&
            grant.ScopeKind == "DeploymentWide" &&
            !bindings.Any(binding => binding.GrantId == grant.Id) &&
            activeUsers.Any(user => user.Id == userId && user.IsActive) &&
            roleDefinitions.Any(role =>
                role.Id == grant.RoleId && role.IsActive &&
                rolePermissions.Any(rolePermission =>
                    rolePermission.RoleId == role.Id &&
                    rolePermission.Permission == permission)),
            cancellationToken);
    }

    private static bool ValidResourceQuery(Guid userId, string permission) =>
        userId != Guid.Empty &&
        ProductPermissionCatalog.Definitions.Any(item =>
            item.Key == permission && item.ScopeKind == "ResourceScoped");

    private IQueryable<AssetEntity> AuthorizedAssets(
        Guid userId, string permission)
    {
        var activeUsers = db.Users.AsNoTracking();
        var assignments = db.AccessGrants.AsNoTracking();
        var bindings = db.AccessGrantAssets.AsNoTracking();
        var roleDefinitions = db.RoleDefinitions.AsNoTracking();
        var rolePermissions = db.RolePermissions.AsNoTracking();
        var activeAssets = db.Assets.AsNoTracking()
            .Where(asset => asset.IsActive);

        return activeAssets
            .Where(asset => activeUsers.Any(user =>
                user.Id == userId && user.IsActive) &&
                assignments.Any(grant =>
                    grant.SubjectKind == "User" &&
                    grant.UserId == userId &&
                    grant.GroupSid == null &&
                    grant.Id != Guid.Empty && grant.RoleId != Guid.Empty &&
                    grant.IsActive && grant.Revision >= 1 &&
                    roleDefinitions.Any(role => role.Id == grant.RoleId &&
                        role.IsActive && rolePermissions.Any(rolePermission =>
                            rolePermission.RoleId == role.Id &&
                            rolePermission.Permission == permission)) &&
                    (
                        (grant.ScopeKind == "AllAssets" &&
                         !bindings.Any(binding => binding.GrantId == grant.Id)) ||
                        (grant.ScopeKind == "ExactAssets" &&
                         bindings.Any(binding => binding.GrantId == grant.Id &&
                             binding.AssetId == asset.Id) &&
                         bindings.Count(binding => binding.GrantId == grant.Id) <=
                             ScopedGrantEvaluator.MaximumExactAssetsPerGrant &&
                         !bindings.Any(binding => binding.GrantId == grant.Id &&
                             binding.AssetId == Guid.Empty) &&
                         !bindings.Any(binding => binding.GrantId == grant.Id &&
                             !activeAssets.Any(other => other.Id == binding.AssetId)))
                    )));
    }
}
