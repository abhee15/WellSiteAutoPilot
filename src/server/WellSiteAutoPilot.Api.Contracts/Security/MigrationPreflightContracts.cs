namespace WellSiteAutoPilot.Api.Contracts.Security;

public sealed record LegacyMigrationDifferenceResponse(
    Guid? AssetId,
    string Permission,
    bool LegacyAllowed,
    bool RecordedGrantAllowed);

/// <summary>
/// Diagnostic snapshot of one user's legacy-versus-recorded grant parity.
/// Never represents an approved, locked or persisted authorization cutover.
/// </summary>
public sealed record LegacyMigrationPreflightResponse(
    Guid UserId,
    int ActiveAssetCount,
    int ExpectedLegacyGrantCount,
    int RecordedGrantCount,
    int ExpansionCount,
    int ReductionCount,
    IReadOnlyCollection<LegacyMigrationDifferenceResponse> DifferenceSamples,
    bool RecordedGrantsMatchLegacy,
    bool IsDiagnosticOnly);

public sealed record DeploymentMigrationUserResponse(
    Guid UserId,
    int ExpectedLegacyGrantCount,
    int RecordedGrantCount,
    int ExpansionCount,
    int ReductionCount,
    bool RecordedGrantsMatchLegacy);

public sealed record DeploymentMigrationPreflightResponse(
    int ActiveUserCount,
    int ActiveAssetCount,
    int MatchingUserCount,
    int ExpansionCount,
    int ReductionCount,
    IReadOnlyCollection<DeploymentMigrationUserResponse> Users,
    bool AllActiveUsersMatchRecordedGrants,
    bool IsDiagnosticOnly);
