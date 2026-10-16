using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>
/// Each user, role and Asset is sampled from one database snapshot.
/// No migration grants are written; neither a passing snapshot nor an
/// inspection result authorizes an automatic or unattended cutover.
/// </summary>
public sealed record DeploymentMigrationSnapshot(
    int ActiveAssetCount,
    IReadOnlyCollection<LegacyMigrationSnapshot> ActiveUsers);

public interface IDeploymentMigrationSnapshotRepository
{
    Task<DeploymentMigrationSnapshot> CaptureAsync(
        Guid administratorId, CancellationToken cancellationToken = default);
}

public sealed record DeploymentMigrationUserResult(
    Guid UserId,
    int ExpectedLegacyGrantCount,
    int RecordedGrantCount,
    int ExpansionCount,
    int ReductionCount,
    bool RecordedGrantsMatchLegacy);

public sealed record DeploymentMigrationPreflightResult(
    int ActiveUserCount,
    int ActiveAssetCount,
    int MatchingUserCount,
    int ExpansionCount,
    int ReductionCount,
    IReadOnlyCollection<DeploymentMigrationUserResult> Users,
    bool AllActiveUsersMatchRecordedGrants,
    bool IsDiagnosticOnly = true);

public sealed class DeploymentMigrationPreflightService(
    AccessGrantAdministrationService administration,
    IDeploymentMigrationSnapshotRepository snapshots,
    ILegacyMigrationPreflightAuditRepository audit,
    TimeProvider clock)
{
    public async Task<DeploymentMigrationPreflightResult> InspectAsync(
        SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        await administration.RequireAdministratorAsync(actor, cancellationToken);
        if (actor.UserId is not { } administratorId)
        {
            throw new WellSiteAutoPilotException(
                "SECURITY_ADMIN_REQUIRED", FailureKind.Authorization,
                "An active administrator is required.");
        }

        // The repository repeats the administrator check inside the same
        // repeatable-read transaction used for the entire active-user set.
        var snapshot = await snapshots.CaptureAsync(
            administratorId, cancellationToken);
        var result = new List<DeploymentMigrationUserResult>();
        var expansions = 0;
        var reductions = 0;
        var matches = 0;

        foreach (var user in snapshot.ActiveUsers.OrderBy(item => item.User.Id))
        {
            var parity = LegacyMigrationPreflightEvaluator.Evaluate(user);
            expansions = checked(expansions + parity.ExpansionCount);
            reductions = checked(reductions + parity.ReductionCount);
            if (parity.RecordedGrantsMatchLegacy) matches++;
            result.Add(new DeploymentMigrationUserResult(
                parity.UserId, parity.ExpectedLegacyGrantCount,
                parity.RecordedGrantCount, parity.ExpansionCount,
                parity.ReductionCount, parity.RecordedGrantsMatchLegacy));
        }

        var response = new DeploymentMigrationPreflightResult(
            result.Count, snapshot.ActiveAssetCount, matches,
            expansions, reductions, result,
            matches == result.Count,
            true);
        await audit.RecordDeploymentAsync(response, actor, clock.GetUtcNow(), cancellationToken);
        return response;
    }
}
