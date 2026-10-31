using WellSiteAutoPilot.Domain.Control;
using WellSiteAutoPilot.Domain.Recommendations;

namespace WellSiteAutoPilot.Application.Control;

public sealed class AutonomousControlOptions
{
    public const string SectionName = "Control:Autonomous";

    // Deployment-level master switch. Off unless an operator turns it on in configuration.
    public bool Enabled { get; set; }

    // Policies created through the API use the DryRun sink instead of the Simulator. Still never writes.
    public bool DryRunSink { get; set; }
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

    // Asks the target what it actually holds for this dispatch (by idempotency key). Unknown when it cannot say.
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
    string Origin = Domain.Control.ControlOrigins.Autonomous);

public interface IControlRepository
{
    Task<AutonomyPolicy?> GetPolicyAsync(Guid configuredLogicId, CancellationToken cancellationToken = default);

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
        DateTimeOffset nowUtc, decimal? readbackValue = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically rejects a still-Created recommendation and records a Blocked ControlAction (no sink call).
    /// Returns false when someone else already decided the recommendation.
    /// </summary>
    Task<bool> RecordBlockedAsync(RecommendationRecord recommendation, string gateCode, string sinkKind,
        DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set transition for readback and operator resolution; null when the action was not in a rom status.</summary>
    Task<ControlActionRecord?> TransitionAsync(Guid actionId, IReadOnlyCollection<ControlActionStatus> from,
        ControlActionStatus target, string gateCode, decimal? readbackValue, string actor, string? note, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<ControlActionRecord?> GetActionAsync(Guid actionId, CancellationToken cancellationToken = default);

    Task<ControlActionRecord?> GetActionByRecommendationAsync(Guid recommendationId,
        CancellationToken cancellationToken = default);

    /// <summary>Audit-only refusal for an approved recommendation; the recommendation stays Approved so it can be retried.</summary>
    Task RecordRefusalAsync(RecommendationRecord recommendation, string gateCode, string actor, DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ControlActionRecord>> ListActionsAsync(Guid? configuredLogicId, int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> ListExecutionsWithUndecidedAutonomousRecommendationsAsync(int limit,
        CancellationToken cancellationToken = default);
}
