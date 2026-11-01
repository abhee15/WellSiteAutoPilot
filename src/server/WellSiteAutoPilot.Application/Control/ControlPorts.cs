using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Control;
using WellSiteAutoPilot.Domain.Recommendations;

namespace WellSiteAutoPilot.Application.Control;

public sealed class AutonomousControlOptions
{
    public const string SectionName = "Control:Autonomous";

    // Deployment-level master switch. Off unless an operator turns it on in configuration.
    public bool Enabled { get; set; }
}

public sealed record ControlDispatchRequest(
    Guid ActionId,
    Guid AssetId,
    string Command,
    string? Quantity,
    decimal? Value,
    string? Unit,
    string IdempotencyKey);

public sealed record ControlDispatchResult(bool Accepted, string Reference, string? Detail = null);

// The only way a ControlAction leaves the platform. Implementations must be idempotent per IdempotencyKey.
public interface IControlCommandSink
{
    string Kind { get; }

    Task<ControlDispatchResult> DispatchAsync(
        ControlDispatchRequest request,
        CancellationToken cancellationToken = default);
}

public enum ControlClaimOutcome
{
    Claimed,
    RateLimited,
    AlreadyDecided
}

public sealed record ControlClaim(
    RecommendationRecord Recommendation,
    string SinkKind,
    int MaxActionsPerWindow,
    int WindowSeconds,
    DateTimeOffset NowUtc);

public interface IControlRepository
{
    // System read used by the gate itself. Never expose this to a request: use GetPolicyAuthorizedAsync.
    Task<AutonomyPolicy?> GetPolicyAsync(Guid configuredLogicId, CancellationToken cancellationToken = default);

    // Returns the policy only when the subject may read every Asset the Configured Logic touches.
    Task<AutonomyPolicy?> GetPolicyAuthorizedAsync(AccessSubject subject, Guid configuredLogicId,
        CancellationToken cancellationToken = default);

    Task UpsertPolicyAsync(AutonomyPolicy policy, CancellationToken cancellationToken = default);

    Task<ControlKillSwitch> GetKillSwitchAsync(CancellationToken cancellationToken = default);

    Task SetKillSwitchAsync(bool engaged, string actor, string? reason, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically moves a Created Autonomous recommendation to Approved (auto-approved by the gate),
    /// enforces the per-configuration rate limit, and records a Requested ControlAction.
    /// </summary>
    Task<(ControlClaimOutcome Outcome, ControlActionRecord? Action)> TryClaimAsync(
        ControlClaim claim, CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid actionId, ControlActionStatus status, string gateCode, string? sinkReference,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically rejects a still-Created recommendation and records a Blocked ControlAction (no sink call).
    /// Returns false when someone else already decided the recommendation.
    /// </summary>
    Task<bool> RecordBlockedAsync(RecommendationRecord recommendation, string gateCode, string sinkKind,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    // System read (diagnostics, tests). Never expose this to a request: use ListActionsAuthorizedAsync.
    Task<IReadOnlyCollection<ControlActionRecord>> ListActionsAsync(Guid? configuredLogicId, int limit,
        CancellationToken cancellationToken = default);

    // Scope is applied in SQL before the limit: only actions whose Asset the subject may read as a Recommendation.
    Task<IReadOnlyCollection<ControlActionRecord>> ListActionsAuthorizedAsync(AccessSubject subject,
        Guid? configuredLogicId, int limit, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> ListExecutionsWithUndecidedAutonomousRecommendationsAsync(int limit,
        CancellationToken cancellationToken = default);
}
