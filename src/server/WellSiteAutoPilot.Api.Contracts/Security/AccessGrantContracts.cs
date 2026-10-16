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
    IReadOnlyCollection<Guid>? AssetIds,
    long ExpectedRoleRevision);

