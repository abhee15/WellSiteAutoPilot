using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Recommendations;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>
/// Composable SQL predicates for direct-user grants. Permission and Asset scope
/// always match within one active grant. Apply ordering/paging/projection only
/// after these predicates. This is query preparation for #76, not a runtime
/// authority switch or a transaction boundary for authorizing mutations.
/// </summary>
public static class ScopedAccessQueries
{
    public static IQueryable<AssetEntity> Assets(
        WellSiteAutoPilotDbContext db, Guid userId, string permission)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (userId == Guid.Empty || !ProductPermissionCatalog.Definitions.Any(item =>
                item.Key == permission && item.ScopeKind == "ResourceScoped"))
            return db.Assets.AsNoTracking().Where(_ => false);

        var activeUsers = db.Users.AsNoTracking();
        var assignments = db.AccessGrants.AsNoTracking();
        var bindings = db.AccessGrantAssets.AsNoTracking();
        var roleDefinitions = db.RoleDefinitions.AsNoTracking();
        var rolePermissions = db.RolePermissions.AsNoTracking();
        var activeAssets = db.Assets.AsNoTracking()
            .Where(asset => asset.IsActive && asset.Id != Guid.Empty);

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

    public static IQueryable<ConfiguredLogicEntity> ConfiguredLogic(
        WellSiteAutoPilotDbContext db, Guid userId, string permission)
    {
        ArgumentNullException.ThrowIfNull(db);
        var query = db.ConfiguredLogicDefinitions.AsNoTracking();
        if (permission is not (WellSitePermissions.ConfiguredLogicRead or
            WellSitePermissions.ConfiguredLogicManage))
            return query.Where(_ => false);

        var assets = Assets(db, userId, permission);
        var revisions = db.ConfiguredLogicRevisions.AsNoTracking();
        var bindings = db.ConfiguredLogicAssetBindings.AsNoTracking();
        var inputs = db.ConfiguredLogicDataBindings.AsNoTracking();
        // Detail responses disclose every revision, not just the active one.
        // Every revision must have a target; check input source Assets too.
        return query.Where(definition =>
            revisions.Any(revision => revision.ConfiguredLogicId == definition.Id) &&
            !revisions.Any(revision => revision.ConfiguredLogicId == definition.Id &&
                (!bindings.Any(binding => binding.RevisionId == revision.Id) ||
                 bindings.Any(binding => binding.RevisionId == revision.Id &&
                     !assets.Any(asset => asset.Id == binding.AssetId)) ||
                 inputs.Any(input => input.RevisionId == revision.Id &&
                     !assets.Any(asset => asset.Id == input.AssetId)))));
    }

    public static IQueryable<ExecutionEntity> Executions(
        WellSiteAutoPilotDbContext db, Guid userId, string permission)
    {
        ArgumentNullException.ThrowIfNull(db);
        var query = db.Executions.AsNoTracking();
        if (permission is not (WellSitePermissions.ExecutionsRead or
            WellSitePermissions.ExecutionsRequest))
            return query.Where(_ => false);

        var assets = Assets(db, userId, permission);
        var scopes = db.ExecutionAssetScopes.AsNoTracking();
        // V1 discloses its target column; V2 discloses an immutable snapshot.
        // Do not authorize from the configuration's current, mutable bindings.
        return query.Where(execution =>
            (execution.RequestContractVersion == 1 || execution.RequestContractVersion == 2) &&
            (execution.RequestContractVersion != 1 || execution.AssetId != null) &&
            (execution.AssetId == null || assets.Any(asset => asset.Id == execution.AssetId)) &&
            scopes.Any(scope => scope.ExecutionId == execution.Id) &&
            !scopes.Any(scope => scope.ExecutionId == execution.Id &&
                !assets.Any(asset => asset.Id == scope.AssetId)));
    }

    public static IQueryable<RecommendationEntity> Recommendations(
        WellSiteAutoPilotDbContext db, Guid userId, string permission)
    {
        ArgumentNullException.ThrowIfNull(db);
        var query = db.Recommendations.AsNoTracking();
        if (permission is not (WellSitePermissions.RecommendationsRead or
            WellSitePermissions.RecommendationsDecide))
            return query.Where(_ => false);

        var assets = Assets(db, userId, permission);
        return query.Where(recommendation =>
            assets.Any(asset => asset.Id == recommendation.AssetId));
    }
}
