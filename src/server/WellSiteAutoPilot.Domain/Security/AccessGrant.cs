namespace WellSiteAutoPilot.Domain.Security;

public enum AccessGrantSubjectKind
{
    User = 0,
    WindowsGroup = 1
}

public enum AccessGrantScopeKind
{
    ExactAssets = 0,
    DeploymentWide = 1,
    AllAssets = 2
}

/// <summary>
/// Role and scope are one indivisible assignment. Never independently union
/// roles across this collection and Asset IDs from other assignments.
/// </summary>
public sealed record AccessGrant(
    Guid Id,
    AccessGrantSubjectKind SubjectKind,
    Guid? UserId,
    string? GroupSid,
    Guid RoleId,
    AccessGrantScopeKind ScopeKind,
    IReadOnlyCollection<Guid> AssetIds,
    bool IsActive,
    long Revision);

public sealed record EffectiveAccessDecision(
    bool Allowed,
    string ReasonCode,
    Guid? GrantedByAssignmentId = null,
    AccessGrantSubjectKind? GrantedBySubjectKind = null);
