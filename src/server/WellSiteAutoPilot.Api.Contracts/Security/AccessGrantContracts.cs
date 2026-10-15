namespace WellSiteAutoPilot.Api.Contracts.Security;

public sealed record AccessGrantResponse(
    Guid GrantId,
    string SubjectKind,
    Guid? UserId,
    string? GroupSid,
    Guid RoleId,
    string ScopeKind,
    IReadOnlyCollection<Guid> AssetIds,
    bool IsActive,
    long Revision);

public sealed record DeactivateUserAccessGrantRequest(long ExpectedRevision);

public sealed record CreateUserAccessGrantRequest(
    Guid RoleId,
    string ScopeKind,
    IReadOnlyCollection<Guid>? AssetIds);

public sealed record PermissionAccessComparisonResponse(
    string Permission,
    string ScopeKind,
    bool LegacyAllowed,
    bool ProposedAllowed,
    string ProposedReasonCode,
    Guid? ProposedGrantId);

public sealed record ScopedAccessInspectionResponse(
    Guid UserId,
    Guid? AssetId,
    string? AssetName,
    IReadOnlyCollection<PermissionAccessComparisonResponse> Permissions,
    int WideningCount,
    int ReductionCount,
    bool IsDiagnosticOnly);

public sealed record ProposedAuthorizedAssetResponse(Guid Id, string Name);

public sealed record ProposedAssetAccessResponse(
    Guid UserId,
    string Permission,
    int Offset,
    int Limit,
    IReadOnlyCollection<ProposedAuthorizedAssetResponse> Assets,
    bool IsDiagnosticOnly);

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

public sealed record MaterializeLegacyGrantsRequest(bool Confirmed);

/// <summary>
/// Successful audit-only grant materialization. Runtime authorization
/// deliberately remains legacy until an independently verified cutover.
/// </summary>
public sealed record MaterializeLegacyGrantsResponse(
    int ActiveUsersInspected,
    int ActiveAssetsInspected,
    int GrantsCreated,
    int UsersAlreadyMatching,
    bool RecordedGrantsMatchLegacy,
    bool LiveAuthorizationUnchanged);

public sealed record RollbackLegacyGrantsRequest(bool Confirmed);

/// <summary>
/// Deactivation receipt for migration-origin entitlements only. Existing
/// production RBAC, historical audit events and customer grants are retained.
/// </summary>
public sealed record RollbackLegacyGrantsResponse(
    int MigratedGrantsInspected,
    int GrantsDeactivated,
    bool LiveAuthorizationUnchanged);
