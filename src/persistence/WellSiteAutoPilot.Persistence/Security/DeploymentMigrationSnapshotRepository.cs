using System.Data;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Persistence.Security;

/// <summary>
/// Consistent, bounded, READ ONLY deployment preflight. A single transaction
/// protects comparison from cross-table and cross-user snapshot skew.
/// Not suitable for a write migration without separate fencing/locking.
/// </summary>
public sealed class DeploymentMigrationSnapshotRepository(
    WellSiteAutoPilotDbContext db) : IDeploymentMigrationSnapshotRepository
{
    public const int MaximumUsers = 100;

    public async Task<DeploymentMigrationSnapshot> CaptureAsync(
        Guid administratorId, CancellationToken cancellationToken = default)
    {
        if (administratorId == Guid.Empty)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_ADMIN_REQUIRED", FailureKind.Authorization,
                "An active administrator is required.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        var result = await CaptureInExistingTransactionAsync(
            administratorId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>
    /// Shared bounded snapshot implementation. Write-side migration must own
    /// the outer SERIALIZABLE transaction and must not run nested transactions.
    /// </summary>
    public async Task<DeploymentMigrationSnapshot> CaptureInExistingTransactionAsync(
        Guid administratorId, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Deployment entitlement snapshot requires an active transaction.");
        }

        var authorized = await db.Users.AsNoTracking().AnyAsync(
            user => user.Id == administratorId && user.IsActive,
            cancellationToken) &&
            await db.UserRoles.AsNoTracking().AnyAsync(
                role => role.UserId == administratorId && role.Role == "Admin",
                cancellationToken);
        if (!authorized)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_ADMIN_REQUIRED", FailureKind.Authorization,
                "The administrator is inactive or no longer has Admin access.");
        }

        // Detect rather than silently skip the 101st account. Counting ALL
        // users includes deactivated accounts so the snapshot cannot
        // accidentally be mistaken for an unrestricted population.
        var userRepository = new UserAccessRepository(db);
        var allAccounts = await userRepository.ListAsync(
            MaximumUsers + 1, cancellationToken);
        if (allAccounts.Count > MaximumUsers)
        {
            throw TooLarge("User inventory exceeds the complete verification limit.");
        }

        var activeAssets = await db.Assets.AsNoTracking()
            .Where(asset => asset.IsActive)
            .OrderBy(asset => asset.Id)
            .Select(asset => asset.Id)
            .Take(LegacyMigrationSnapshotRepository.MaximumActiveAssets + 1)
            .ToArrayAsync(cancellationToken);
        if (activeAssets.Length > LegacyMigrationSnapshotRepository.MaximumActiveAssets)
        {
            throw TooLarge("Asset inventory exceeds the complete verification limit.");
        }

        var roleRepository = new RoleCatalogRepository(db);
        var roles = await roleRepository.ListAsync(
            LegacyMigrationSnapshotRepository.MaximumRoles + 1,
            cancellationToken);
        if (roles.Count > LegacyMigrationSnapshotRepository.MaximumRoles)
        {
            throw TooLarge("Role catalog exceeds the complete verification limit.");
        }

        var assets = activeAssets.ToHashSet();
        var grantRepository = new AccessGrantRepository(db);
        var snapshots = new List<LegacyMigrationSnapshot>();

        // Inactive users cannot execute logic and are not counted as allowed
        // subjects. This is NOT evidence that reactivation is safe.
        foreach (var user in allAccounts.Where(item => item.IsActive))
        {
            var assigned = await grantRepository.ListForUserAsync(
                user.Id, cancellationToken);
            snapshots.Add(new LegacyMigrationSnapshot(
                user, roles, assets, assigned));
        }

        return new DeploymentMigrationSnapshot(assets.Count, snapshots);
    }

    private static WellSiteAutoPilotException TooLarge(string reason) =>
        new("SECURITY_MIGRATION_PREFLIGHT_TOO_LARGE",
            FailureKind.Validation, reason);
}
