using WellSiteAutoPilot.Application.Security;

namespace WellSiteAutoPilot.Application.Scheduling;

/// <summary>
/// Designated service principal a schedule acts as for live provider reads. It is a normal security user
/// (<c>security.users</c>) whose scoped grants bound what schedules may read; there is no ambient authority.
/// </summary>
public sealed class ScheduledReadAuthorityOptions
{
    public const string SectionName = "Scheduler:ReadAuthority";

    public Guid? ServicePrincipalUserId { get; set; }
}

/// <summary>Outcome of resolving the delegated read authority for one scheduled occurrence.</summary>
public sealed record DelegatedReadAuthority(AccessSubject? Subject, Guid? PrincipalId, string? RefusalCode)
{
    public static DelegatedReadAuthority None { get; } = new(null, null, null);
}

public interface IScheduledReadAuthorityProvider
{
    Task<DelegatedReadAuthority> ResolveAsync(Guid configuredLogicId, Guid revisionId, DateTimeOffset scheduledForUtc,
        CancellationToken cancellationToken = default);

    Task RecordUseAsync(Guid configuredLogicId, Guid revisionId, DateTimeOffset scheduledForUtc, Guid principalId,
        Guid executionId, CancellationToken cancellationToken = default);
}

public interface IDelegatedAuthorityAuditWriter
{
    Task WriteAsync(DateTimeOffset atUtc, string action, Guid principalId, Guid configuredLogicId,
        object details, CancellationToken cancellationToken = default);
}

/// <summary>
/// Re-evaluates the service principal on every occurrence (fail closed). Deactivating the user, removing the
/// configuration or clearing it revokes the delegation immediately; grants themselves are re-checked by the
/// read-plan pinning step, and every refusal or use is audited.
/// </summary>
public sealed class ScheduledReadAuthorityProvider(
    ScheduledReadAuthorityOptions options,
    IUserIdentityRepository identities,
    IDelegatedAuthorityAuditWriter audit,
    TimeProvider timeProvider) : IScheduledReadAuthorityProvider
{
    public const string PrincipalRevoked = "SCHEDULER_READ_AUTHORITY_REVOKED";

    public async Task<DelegatedReadAuthority> ResolveAsync(Guid configuredLogicId, Guid revisionId,
        DateTimeOffset scheduledForUtc, CancellationToken cancellationToken = default)
    {
        if (options.ServicePrincipalUserId is not { } principalId || principalId == Guid.Empty)
        {
            return DelegatedReadAuthority.None;
        }

        var identity = await identities.GetIdentityAsync(principalId, cancellationToken);
        if (identity is null || !identity.IsActive)
        {
            await audit.WriteAsync(timeProvider.GetUtcNow(), "scheduler.delegation.refused", principalId, configuredLogicId,
                new { RevisionId = revisionId, ScheduledForUtc = scheduledForUtc, Reason = identity is null ? "principal-missing" : "principal-inactive" },
                cancellationToken);
            return new DelegatedReadAuthority(null, principalId, PrincipalRevoked);
        }

        return new DelegatedReadAuthority(new AccessSubject(principalId), principalId, null);
    }

    public Task RecordUseAsync(Guid configuredLogicId, Guid revisionId, DateTimeOffset scheduledForUtc, Guid principalId,
        Guid executionId, CancellationToken cancellationToken = default) =>
        audit.WriteAsync(timeProvider.GetUtcNow(), "scheduler.delegation.used", principalId, configuredLogicId,
            new { RevisionId = revisionId, ScheduledForUtc = scheduledForUtc, ExecutionId = executionId }, cancellationToken);
}
