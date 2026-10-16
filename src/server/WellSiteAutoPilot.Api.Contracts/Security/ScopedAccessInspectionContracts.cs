namespace WellSiteAutoPilot.Api.Contracts.Security;

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

