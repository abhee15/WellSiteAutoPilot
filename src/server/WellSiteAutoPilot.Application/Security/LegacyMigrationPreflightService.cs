using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>
/// All data in this snapshot is collected under one database repeatable-read
/// transaction, including the actor's administrator status. No writes occur.
/// A snapshot is not a durable authorization cutover or a future-proof proof
/// that the database has remained unchanged after the transaction completes.
/// </summary>
public sealed record LegacyMigrationSnapshot(
    UserAccessProfile User,
    IReadOnlyCollection<RoleDefinition> Roles,
    IReadOnlySet<Guid> ActiveAssetIds,
    IReadOnlyCollection<AccessGrant> RecordedGrants);

public interface ILegacyMigrationSnapshotRepository
{
    Task<LegacyMigrationSnapshot> CaptureAsync(
        Guid userId, Guid administratorId,
        CancellationToken cancellationToken = default);
}

public sealed record LegacyMigrationDifference(
    Guid? AssetId,
    string Permission,
    bool LegacyAllowed,
    bool RecordedGrantAllowed);

public sealed record LegacyMigrationPreflightResult(
    Guid UserId,
    int ActiveAssetCount,
    int ExpectedLegacyGrantCount,
    int RecordedGrantCount,
    int ExpansionCount,
    int ReductionCount,
    IReadOnlyCollection<LegacyMigrationDifference> DifferenceSamples,
    bool RecordedGrantsMatchLegacy,
    bool IsDiagnosticOnly = true);

/// <summary>
/// Read-only single-user migration preflight. Never writes inferred grants or
/// enables them. Every known product permission is compared against every
/// active Asset in an authoritative, bounded database snapshot.
/// </summary>
public sealed class LegacyMigrationPreflightService(
    AccessGrantAdministrationService administration,
    ILegacyMigrationSnapshotRepository snapshots)
{
    private const int MaximumDifferenceSamples = 20;

    public async Task<LegacyMigrationPreflightResult> InspectAsync(
        Guid userId, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        await administration.RequireAdministratorAsync(actor, cancellationToken);
        if (userId == Guid.Empty || actor.UserId is not { } administratorId)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_MIGRATION_PREFLIGHT_INVALID",
                FailureKind.Validation, "A valid user and administrator are required.");
        }

        // Snapshot repository revalidates the actor inside its transaction.
        var snapshot = await snapshots.CaptureAsync(
            userId, administratorId, cancellationToken);

        // The planner proves that a newly generated set of built-in grants
        // precisely matches the legacy role×Asset permission product for this
        // snapshot. Persisted customer grants are separately compared below.
        var expected = LegacyGrantMigrationPlanner.PreviewForKnownAssets(
            snapshot.User, snapshot.Roles.Where(role => role.IsSystem).ToArray(),
            snapshot.ActiveAssetIds);

        var expansionCount = 0;
        var reductionCount = 0;
        var samples = new List<LegacyMigrationDifference>();

        Compare(null);
        foreach (var assetId in snapshot.ActiveAssetIds.OrderBy(id => id))
        {
            Compare(assetId);
        }

        return new LegacyMigrationPreflightResult(
            userId, snapshot.ActiveAssetIds.Count, expected.Count,
            snapshot.RecordedGrants.Count(item => item.IsActive),
            expansionCount, reductionCount, samples,
            expansionCount == 0 && reductionCount == 0);

        void Compare(Guid? assetId)
        {
            var comparison = ScopedAccessComparisonEvaluator.Compare(
                snapshot.User, assetId, null, snapshot.ActiveAssetIds,
                snapshot.RecordedGrants, snapshot.Roles);
            expansionCount = checked(expansionCount + comparison.WideningCount);
            reductionCount = checked(reductionCount + comparison.ReductionCount);
            foreach (var item in comparison.Permissions)
            {
                if (item.LegacyAllowed == item.ProposedAllowed ||
                    samples.Count >= MaximumDifferenceSamples)
                {
                    continue;
                }

                samples.Add(new LegacyMigrationDifference(
                    item.ScopeKind == "ResourceScoped" ? assetId : null,
                    item.Permission, item.LegacyAllowed, item.ProposedAllowed));
            }
        }
    }
}
