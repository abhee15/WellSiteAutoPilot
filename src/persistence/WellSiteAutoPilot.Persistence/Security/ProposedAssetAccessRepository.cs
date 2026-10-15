using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>
/// Proposed entitlement SQL projection ONLY; it is not the live authorization
/// source. Each matching resource permission comes from a single active,
/// direct-user role-and-scope grant. Filtering happens before Order/Skip/Take.
/// </summary>
public sealed class ProposedAssetAccessRepository(WellSiteAutoPilotDbContext db)
    : IProposedAssetAccessRepository
{
    public async Task<IReadOnlyCollection<ProposedAuthorizedAsset>> ListAsync(
        Guid userId, string permission, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        // Defence in depth for non-HTTP callers: reject unknown and global
        // permissions without broadening them to a resource listing.
        if (userId == Guid.Empty ||
            !ProductPermissionCatalog.Definitions.Any(p =>
                p.Key == permission && p.ScopeKind == "ResourceScoped") ||
            offset < 0 || offset > 1_000_000 || limit is < 1 or > 500)
        {
            return [];
        }

        var activeUsers = db.Users.AsNoTracking();
        var assignments = db.AccessGrants.AsNoTracking();
        var bindings = db.AccessGrantAssets.AsNoTracking();
        var roleDefinitions = db.RoleDefinitions.AsNoTracking();
        var rolePermissions = db.RolePermissions.AsNoTracking();
        var activeAssets = db.Assets.AsNoTracking().Where(asset => asset.IsActive);

        return await activeAssets
            .Where(asset => activeUsers.Any(user =>
                user.Id == userId && user.IsActive) &&
                assignments.Any(grant =>
                    grant.SubjectKind == "User" &&
                    grant.UserId == userId &&
                    grant.GroupSid == null &&
                    grant.Id != Guid.Empty && grant.RoleId != Guid.Empty &&
                    grant.IsActive && grant.Revision >= 1 &&
                    roleDefinitions.Any(role => role.Id == grant.RoleId &&
                        role.Id != Guid.Empty && role.IsActive && rolePermissions.Any(rolePermission =>
                            rolePermission.RoleId == role.Id &&
                            rolePermission.Permission == permission)) &&
                    (
                        (grant.ScopeKind == "AllAssets" &&
                         !bindings.Any(binding => binding.GrantId == grant.Id)) ||
                        (grant.ScopeKind == "ExactAssets" &&
                         bindings.Any(binding => binding.GrantId == grant.Id &&
                             binding.AssetId == asset.Id) &&
                         bindings.Count(binding => binding.GrantId == grant.Id) <= 500 &&
                         !bindings.Any(binding => binding.GrantId == grant.Id &&
                             binding.AssetId == Guid.Empty) &&
                         !bindings.Any(binding => binding.GrantId == grant.Id &&
                             !activeAssets.Any(other => other.Id == binding.AssetId)))
                    )))
            .OrderBy(asset => asset.Name).ThenBy(asset => asset.Id)
            .Skip(offset).Take(limit)
            .Select(asset => new ProposedAuthorizedAsset(asset.Id, asset.Name))
            .ToArrayAsync(cancellationToken);
    }
}
