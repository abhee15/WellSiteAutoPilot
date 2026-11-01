using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Control;
using WellSiteAutoPilot.Domain.Recommendations;

namespace WellSiteAutoPilot.Application.Control;

public sealed class AutonomousControlOptions
{
    public const string SectionName = "Control:Autonomous";

    // Deployment-level master switch. Off unless an operator turns it on in configuration.
    public bool Enabled { get; set; }

    // An approved recommendation describes the plant as it was when the module ran. Executing it later than this
    // risks acting on stale data, so the gate refuses older ones; a person re-runs the logic instead.
    public TimeSpan ApprovedRecommendationMaxAge { get; set; } = TimeSpan.FromMinutes(30);
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

public sealed record ControlReadback(ControlReadbackState State, decimal? ObservedValue = null);

// The only way a ControlAction leaves the platform. Implementations must be idempotent per IdempotencyKey.
public interface IControlCommandSink
{
    string Kind { get; }

    Task<ControlDispatchResult> DispatchAsync(
        ControlDispatchRequest request,
        CancellationToken cancellationToken = default);

    // Asks the target what it actually holds for this dispatch (found by idempotency key). Unknown when it cannot say;
    // a sink must never guess, because a wrong "Applied" or "NotApplied" is a wrong statement about physical equipment.
    Task<ControlReadback> ReadbackAsync(
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
    DateTimeOffset NowUtc,
    string Origin = ControlOrigins.Autonomous);

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

    Task<ControlActionRecord?> GetActionAsync(Guid actionId, CancellationToken cancellationToken = default);

    Task<ControlActionRecord?> GetActionByRecommendationAsync(Guid recommendationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Audit-only refusal of an approved recommendation. The recommendation stays Approved so a person can fix the
    /// cause (policy, window, kill switch) and execute it again; nothing is dispatched.
    /// </summary>
    Task RecordRefusalAsync(RecommendationRecord recommendation, string gateCode, string actor, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    // System read (diagnostics, tests). Never expose this to a request: use ListActionsAuthorizedAsync.
    Task<IReadOnlyCollection<ControlActionRecord>> ListActionsAsync(Guid? configuredLogicId, int limit,
        CancellationToken cancellationToken = default);

    // True when the subject may decide the recommendation behind this action; false for missing and forbidden alike.
    Task<bool> CanDecideActionAsync(AccessSubject subject, Guid actionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare-and-set transition used by readback and operator resolution. Returns null when the action is not in
    /// one of the <paramref name="from"/> statuses (including when another caller got there first).
    /// </summary>
    Task<ControlActionRecord?> TransitionAsync(Guid actionId, IReadOnlyCollection<ControlActionStatus> from,
        ControlActionStatus target, string gateCode, decimal? readbackValue, string actor, string? note,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    // Scope is applied in SQL before the limit: only actions whose Asset the subject may read as a Recommendation.
    // statuses (when given) filters in SQL before the limit, so a long history cannot push an open action out of the
    // page; oldestFirst orders by age so the action waiting longest is returned first.
    Task<IReadOnlyCollection<ControlActionRecord>> ListActionsAuthorizedAsync(AccessSubject subject,
        Guid? configuredLogicId, IReadOnlyCollection<ControlActionStatus>? statuses, bool oldestFirst, int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> ListExecutionsWithUndecidedAutonomousRecommendationsAsync(int limit,
        CancellationToken cancellationToken = default);
}
