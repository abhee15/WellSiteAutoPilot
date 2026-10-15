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
