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
