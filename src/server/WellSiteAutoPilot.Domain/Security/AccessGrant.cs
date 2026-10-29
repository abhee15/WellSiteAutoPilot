namespace WellSiteAutoPilot.Domain.Security;

public enum AccessGrantSubjectKind
{
    User = 0,
    Group = 1
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
    string? GroupId,
    Guid RoleId,
    AccessGrantScopeKind ScopeKind,
    IReadOnlyCollection<Guid> AssetIds,
    bool IsActive,
    long Revision)
{
    public string? GroupDisplayName { get; init; }
    public string? GroupDomain { get; init; }
    public string? DirectoryId { get; init; } = SubjectKind == AccessGrantSubjectKind.Group ? "windows-ad" : null;
}

public sealed record EffectiveAccessDecision(
    bool Allowed,
    string ReasonCode,
    Guid? GrantedByAssignmentId = null,
    AccessGrantSubjectKind? GrantedBySubjectKind = null);
