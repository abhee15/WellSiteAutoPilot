using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>An assignment target, never evidence of a user's group membership.</summary>
public sealed record AccessGrantSubject
{
    public AccessGrantSubjectKind Kind { get; }
    public Guid? UserId { get; }
    public string? GroupId { get; }
    public string? DirectoryId { get; private init; }
    public string? DisplayName { get; private init; }
    public string? Domain { get; private init; }

    private AccessGrantSubject(AccessGrantSubjectKind kind, Guid? userId, string? groupId)
        => (Kind, UserId, GroupId) = (kind, userId, groupId);

    public static AccessGrantSubject ForUser(Guid userId)
    {
        if (userId == Guid.Empty) throw InvalidSubject();
        return new(AccessGrantSubjectKind.User, userId, null);
    }

    public static AccessGrantSubject ForGroup(string directoryId, string groupId, string? displayName = null, string? domain = null)
    {
        if (!DirectoryGroupIdentity.IsValidDirectory(directoryId) || !DirectoryGroupIdentity.IsValidId(groupId)) throw InvalidSubject();
        return new(AccessGrantSubjectKind.Group, null, groupId) { DirectoryId = directoryId, DisplayName = displayName, Domain = domain };
    }

    public static AccessGrantSubject ForWindowsGroup(string? groupId)
    {
        if (!WindowsSidFormat.IsValid(groupId)) throw InvalidSubject();
        return ForGroup("windows-ad", groupId!.ToUpperInvariant());
    }

    private static WellSiteAutoPilotException InvalidSubject() => new(
        "SECURITY_GRANT_SUBJECT_INVALID", FailureKind.Validation,
        "A valid user ID or canonical Windows group SID is required.");
}
