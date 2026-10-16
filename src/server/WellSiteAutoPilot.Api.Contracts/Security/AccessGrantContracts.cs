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

public sealed record DeactivateAccessGrantRequest(long ExpectedRevision);

public sealed record CreateAccessGrantRequest(
    Guid RoleId,
    string ScopeKind,
    IReadOnlyCollection<Guid>? AssetIds,
    long ExpectedRoleRevision);

