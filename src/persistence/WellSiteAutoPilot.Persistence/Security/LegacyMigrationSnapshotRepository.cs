using System.Data;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>
/// Obtain one bounded, consistent SQL snapshot. This is a READ ONLY
/// preflight, not permission migration, entitlement persistence or cutover.
/// An explicit transaction prevents users/roles/grants/Assets from being
/// sampled at different database versions.
/// </summary>
public sealed class LegacyMigrationSnapshotRepository(WellSiteAutoPilotDbContext db)
    : ILegacyMigrationSnapshotRepository
{
    // The pure evaluator verifies every permission for every active Asset.
    // Raise these bounds only alongside a verified scalable verification plan.
    public const int MaximumActiveAssets = 2000;
    public const int MaximumRoles = 200;

    public async Task<LegacyMigrationSnapshot> CaptureAsync(
        Guid userId, Guid administratorId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || administratorId == Guid.Empty)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_MIGRATION_PREFLIGHT_INVALID",
                FailureKind.Validation, "A valid user and administrator are required.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);

        var isActiveAdmin = await db.Users.AsNoTracking().AnyAsync(
            user => user.Id == administratorId && user.IsActive,
            cancellationToken) &&
            await db.UserRoles.AsNoTracking().AnyAsync(
                role => role.UserId == administratorId && role.Role == "Admin",
                cancellationToken);
        if (!isActiveAdmin)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_ADMIN_REQUIRED",
                FailureKind.Authorization,
                "An active administrator is required for migration preflight.");
        }

        var user = await new UserAccessRepository(db)
            .GetAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_USER_NOT_ACTIVE", FailureKind.NotFound,
                "The user must be active for migration preflight.");
        }

        var activeAssetIds = await db.Assets.AsNoTracking()
            .Where(asset => asset.IsActive)
            .OrderBy(asset => asset.Id)
            .Select(asset => asset.Id)
            .Take(MaximumActiveAssets + 1)
            .ToArrayAsync(cancellationToken);
        if (activeAssetIds.Length > MaximumActiveAssets)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_MIGRATION_PREFLIGHT_TOO_LARGE",
                FailureKind.Validation,
                "The Asset inventory exceeds the complete verification limit.");
        }

        var roles = await new RoleCatalogRepository(db)
            .ListAsync(MaximumRoles + 1, cancellationToken);
        if (roles.Count > MaximumRoles)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_MIGRATION_PREFLIGHT_TOO_LARGE",
                FailureKind.Validation,
                "The role catalog exceeds the complete verification limit.");
        }

        var grants = await new AccessGrantRepository(db)
            .ListForUserAsync(userId, cancellationToken);

        // All queries above share one repeatable-read PostgreSQL snapshot.
        // The returned detached objects remain diagnostic evidence only:
        // concurrent changes after this transaction invalidate cutover use.
        await tx.CommitAsync(cancellationToken);
        return new LegacyMigrationSnapshot(
            user, roles, activeAssetIds.ToHashSet(), grants);
    }
}
