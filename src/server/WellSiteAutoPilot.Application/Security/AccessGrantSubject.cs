using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>An assignment target, never evidence of a user's group membership.</summary>
public sealed record AccessGrantSubject
{
    public AccessGrantSubjectKind Kind { get; }
    public Guid? UserId { get; }
    public string? GroupSid { get; }

    private AccessGrantSubject(AccessGrantSubjectKind kind, Guid? userId, string? groupSid)
        => (Kind, UserId, GroupSid) = (kind, userId, groupSid);

    public static AccessGrantSubject ForUser(Guid userId)
    {
        if (userId == Guid.Empty) throw InvalidSubject();
        return new(AccessGrantSubjectKind.User, userId, null);
    }

    public static AccessGrantSubject ForWindowsGroup(string? groupSid)
    {
        if (!WindowsSidFormat.IsValid(groupSid)) throw InvalidSubject();
        return new(AccessGrantSubjectKind.WindowsGroup, null, groupSid!.ToUpperInvariant());
    }

    private static WellSiteAutoPilotException InvalidSubject() => new(
        "SECURITY_GRANT_SUBJECT_INVALID", FailureKind.Validation,
        "A valid user ID or canonical Windows group SID is required.");
}
