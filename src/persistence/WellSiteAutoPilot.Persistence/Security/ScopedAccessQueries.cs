using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Recommendations;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>
/// Composable SQL predicates for direct-user and verified Windows-group grants. Permission and Asset scope
/// always match within one active grant. Apply ordering/paging/projection only
/// after these predicates. This is query preparation for #76, not a runtime
/// authority switch or a transaction boundary for authorizing mutations.
/// </summary>
public static class ScopedAccessQueries
{
    public static IQueryable<AssetEntity> Assets(
        WellSiteAutoPilotDbContext db, Guid userId, string permission,
        VerifiedWindowsGroupMembership? membership = null, DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (userId == Guid.Empty || !ProductPermissionCatalog.Definitions.Any(item =>
                item.Key == permission && item.ScopeKind == "ResourceScoped"))
            return db.Assets.AsNoTracking().Where(_ => false);

        var assignments = EligibleGrants(db, userId, permission, membership, nowUtc);
        var bindings = db.AccessGrantAssets.AsNoTracking();
        var activeAssets = db.Assets.AsNoTracking()
            .Where(asset => asset.IsActive && asset.Id != Guid.Empty);

        return activeAssets.Where(asset => assignments.Any(grant =>
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

    public static IQueryable<AccessGrantEntity> DeploymentGrants(
        WellSiteAutoPilotDbContext db, Guid userId, string permission,
        VerifiedWindowsGroupMembership? membership = null, DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!ProductPermissionCatalog.Definitions.Any(item =>
                item.Key == permission && item.ScopeKind == "DeploymentWide"))
            return db.AccessGrants.AsNoTracking().Where(_ => false);
        var bindings = db.AccessGrantAssets.AsNoTracking();
        return EligibleGrants(db, userId, permission, membership, nowUtc)
            .Where(grant => grant.ScopeKind == "DeploymentWide" &&
                !bindings.Any(binding => binding.GrantId == grant.Id));
    }

    private static IQueryable<AccessGrantEntity> EligibleGrants(
        WellSiteAutoPilotDbContext db, Guid userId, string permission,
        VerifiedWindowsGroupMembership? membership, DateTimeOffset? nowUtc)
    {
        if (userId == Guid.Empty || !ProductPermissionCatalog.IsKnown(permission))
            return db.AccessGrants.AsNoTracking().Where(_ => false);
        var groupSids = membership is not null && nowUtc is { } observed &&
            membership.IsUsableFor(userId, observed)
            ? membership.GroupSids.ToArray() : Array.Empty<string>();
        var users = db.Users.AsNoTracking();
        var roles = db.RoleDefinitions.AsNoTracking();
        var permissions = db.RolePermissions.AsNoTracking();
        return db.AccessGrants.AsNoTracking().Where(grant =>
            grant.Id != Guid.Empty && grant.RoleId != Guid.Empty && grant.IsActive && grant.Revision >= 1 &&
            users.Any(user => user.Id == userId && user.IsActive) &&
            ((grant.SubjectKind == "User" && grant.UserId == userId && grant.GroupSid == null) ||
             (grant.SubjectKind == "WindowsGroup" && grant.UserId == null && grant.GroupSid != null &&
              groupSids.Contains(grant.GroupSid))) &&
            roles.Any(role => role.Id == grant.RoleId && role.IsActive && role.Revision >= 1 &&
                permissions.Any(item => item.RoleId == role.Id && item.Permission == permission)));
    }

    public static IQueryable<ConfiguredLogicEntity> ConfiguredLogic(
        WellSiteAutoPilotDbContext db, Guid userId, string permission,
        VerifiedWindowsGroupMembership? membership = null, DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        var query = db.ConfiguredLogicDefinitions.AsNoTracking();
        if (permission is not (WellSitePermissions.ConfiguredLogicRead or
            WellSitePermissions.ConfiguredLogicManage))
            return query.Where(_ => false);

        var assets = Assets(db, userId, permission, membership, nowUtc);
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
        WellSiteAutoPilotDbContext db, Guid userId, string permission,
        VerifiedWindowsGroupMembership? membership = null, DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        var query = db.Executions.AsNoTracking();
        if (permission is not (WellSitePermissions.ExecutionsRead or
            WellSitePermissions.ExecutionsRequest))
            return query.Where(_ => false);

        var assets = Assets(db, userId, permission, membership, nowUtc);
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
        WellSiteAutoPilotDbContext db, Guid userId, string permission,
        VerifiedWindowsGroupMembership? membership = null, DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        var query = db.Recommendations.AsNoTracking();
        if (permission is not (WellSitePermissions.RecommendationsRead or
            WellSitePermissions.RecommendationsDecide))
            return query.Where(_ => false);

        var assets = Assets(db, userId, permission, membership, nowUtc);
        return query.Where(recommendation =>
            assets.Any(asset => asset.Id == recommendation.AssetId));
    }
}
