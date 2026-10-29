using System.Collections.Frozen;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>
/// Request-bound proof supplied by the trusted authentication adapter, never
/// by a request DTO/header. Binds enabled token groups to the resolved local
/// user. Observation time bounds reuse of this proof; it is NOT a claim that
/// Active Directory was queried or that an existing Windows token was renewed.
/// </summary>
public class VerifiedGroupMembership
{
    public const int MaximumGroups = 2048;
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(1);

    public string DirectoryId { get; }
    public Guid UserId { get; }
    public IReadOnlySet<string> GroupIds { get; }
    public DateTimeOffset ObservedAtUtc { get; }

    public VerifiedGroupMembership(Guid userId, IEnumerable<string> enabledGroupIds,
        DateTimeOffset observedAtUtc, string directoryId)
    {
        ArgumentNullException.ThrowIfNull(enabledGroupIds);
        var sids = enabledGroupIds.Take(MaximumGroups + 1).ToArray();
        if (userId == Guid.Empty || sids.Length > MaximumGroups ||
            !DirectoryGroupIdentity.IsValidDirectory(directoryId) ||
            sids.Any(id => !DirectoryGroupIdentity.IsValidId(id)))
            throw new WellSiteAutoPilotException("SECURITY_GROUP_PROOF_INVALID", FailureKind.Authorization,
                "Verified group membership is invalid or exceeds the supported bound.");

        DirectoryId = directoryId;
        UserId = userId;
        GroupIds = sids.ToFrozenSet(StringComparer.Ordinal);
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
    }

    public bool IsUsableFor(Guid userId, DateTimeOffset nowUtc) =>
        UserId == userId && userId != Guid.Empty && nowUtc >= ObservedAtUtc &&
        nowUtc - ObservedAtUtc <= MaximumAge;
}
