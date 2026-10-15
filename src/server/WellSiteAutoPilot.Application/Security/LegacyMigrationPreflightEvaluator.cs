using WellSiteAutoPilot.Domain.Security;

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

public static class LegacyMigrationPreflightEvaluator
{
    private const int MaximumDifferenceSamples = 20;

    public static LegacyMigrationPreflightResult Evaluate(
        LegacyMigrationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.User);
        ArgumentNullException.ThrowIfNull(snapshot.Roles);
        ArgumentNullException.ThrowIfNull(snapshot.ActiveAssetIds);
        ArgumentNullException.ThrowIfNull(snapshot.RecordedGrants);

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
            snapshot.User.Id, snapshot.ActiveAssetIds.Count, expected.Count,
            snapshot.RecordedGrants.Count(item => item.IsActive),
            expansionCount, reductionCount, samples,
            expansionCount == 0 && reductionCount == 0);

        void Compare(Guid? assetId)
        {
            var comparison = ScopedAccessComparisonEvaluator.Compare(
                snapshot.User, assetId, null, snapshot.ActiveAssetIds,
                snapshot.RecordedGrants, snapshot.Roles);
            foreach (var item in comparison.Permissions)
            {
                // Deployment-wide capabilities have exactly one decision per
                // user, not one decision per Asset. Never inflate totals.
                if (assetId is not null && item.ScopeKind == "DeploymentWide")
                {
                    continue;
                }
                if (item.LegacyAllowed == item.ProposedAllowed)
                {
                    continue;
                }

                if (item.ProposedAllowed)
                {
                    expansionCount = checked(expansionCount + 1);
                }
                else
                {
                    reductionCount = checked(reductionCount + 1);
                }
                if (samples.Count < MaximumDifferenceSamples)
                {
                    samples.Add(new LegacyMigrationDifference(
                        item.ScopeKind == "ResourceScoped" ? assetId : null,
                        item.Permission, item.LegacyAllowed, item.ProposedAllowed));
                }
            }
        }
    }
}
