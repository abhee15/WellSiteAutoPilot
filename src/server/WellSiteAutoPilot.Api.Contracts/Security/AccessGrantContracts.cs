namespace WellSiteAutoPilot.Api.Contracts.Security;

public sealed record AccessGrantResponse(
    Guid GrantId,
    AccessGrantSubjectKind SubjectKind,
    Guid? UserId,
    string? GroupSid,
    Guid RoleId,
    AccessGrantScopeKind ScopeKind,
    IReadOnlyCollection<Guid> AssetIds,
    bool IsActive,
    long Revision);

public sealed record DeactivateAccessGrantRequest(long ExpectedRevision);

public sealed record CreateAccessGrantRequest(
    Guid RoleId,
    AccessGrantScopeKind ScopeKind,
    IReadOnlyCollection<Guid>? AssetIds,
    long ExpectedRoleRevision);

