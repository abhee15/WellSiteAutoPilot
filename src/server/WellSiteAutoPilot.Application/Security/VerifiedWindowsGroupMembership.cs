using System.Collections.Frozen;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>
/// Request-bound proof supplied by the trusted authentication adapter, never
/// by a request DTO/header. Binds enabled token groups to the resolved local
/// user. Observation time bounds reuse of this proof; it is NOT a claim that
/// Active Directory was queried or that an existing Windows token was renewed.
/// </summary>
public sealed class VerifiedWindowsGroupMembership
{
    public const int MaximumGroups = 2048;
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(1);

    public Guid UserId { get; }
    public IReadOnlySet<string> GroupSids { get; }
    public DateTimeOffset ObservedAtUtc { get; }

    public VerifiedWindowsGroupMembership(Guid userId, IEnumerable<string> enabledGroupSids,
        DateTimeOffset observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(enabledGroupSids);
        var sids = enabledGroupSids.Take(MaximumGroups + 1).ToArray();
        if (userId == Guid.Empty || sids.Length > MaximumGroups ||
            sids.Any(sid => !WindowsSidFormat.IsValid(sid)))
            throw new WellSiteAutoPilotException("SECURITY_GROUP_PROOF_INVALID", FailureKind.Authorization,
                "Verified group membership is invalid or exceeds the supported bound.");

        UserId = userId;
        GroupSids = sids.Select(sid => sid.ToUpperInvariant()).ToFrozenSet(StringComparer.Ordinal);
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
    }

    public bool IsUsableFor(Guid userId, DateTimeOffset nowUtc) =>
        UserId == userId && userId != Guid.Empty && nowUtc >= ObservedAtUtc &&
        nowUtc - ObservedAtUtc <= MaximumAge;
}
