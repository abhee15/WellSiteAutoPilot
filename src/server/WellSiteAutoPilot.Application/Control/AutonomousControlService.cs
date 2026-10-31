using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Recommendations;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Control;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Recommendations;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Control;

public static class GateCodes
{
    public const string Allowed = "GATE_ALLOWED";
    public const string GlobalDisabled = "GATE_AUTONOMOUS_DISABLED";
    public const string KillSwitch = "GATE_KILL_SWITCH";
    public const string NoPolicy = "GATE_NO_POLICY";
    public const string PolicyDisabled = "GATE_POLICY_DISABLED";
    public const string NotAutonomous = "GATE_NOT_AUTONOMOUS_REVISION";
    public const string SourceInvalid = "GATE_SOURCE_INVALID";
    public const string Expired = "GATE_RECOMMENDATION_EXPIRED";
    public const string CommandNotPermitted = "GATE_COMMAND_NOT_PERMITTED";
    public const string ValueRequired = "GATE_VALUE_REQUIRED";
    public const string UnitMismatch = "GATE_UNIT_MISMATCH";
    public const string OutOfBounds = "GATE_VALUE_OUT_OF_BOUNDS";
    public const string RateLimited = "GATE_RATE_LIMITED";
    public const string RealWritesDisabled = "REAL_PROVIDER_WRITES_DISABLED";
    public const string SinkUnavailable = "GATE_SINK_UNAVAILABLE";
    public const string SinkRefused = "SINK_REFUSED";
    public const string SinkFailed = "SINK_FAILED";
    public const string SinkAmbiguous = "SINK_AMBIGUOUS";
    public const string ApprovedNotEnabled = "GATE_APPROVED_EXECUTION_NOT_ENABLED";
    public const string ReadbackConfirmed = "READBACK_CONFIRMED";
    public const string ReadbackMismatch = "READBACK_MISMATCH";
    public const string ReadbackNotApplied = "READBACK_NOT_APPLIED";
    public const string OperatorResolvedApplied = "OPERATOR_RESOLVED_APPLIED";
    public const string OperatorResolvedNotApplied = "OPERATOR_RESOLVED_NOT_APPLIED";
}

public static class ControlActor
{
    public const string Gate = "system:autonomy-gate";
}

public sealed record ControlExecutionResult(ControlActionRecord? Action, string? RefusalCode);

/// <summary>
/// The single path from an Autonomous-mode ControlIntent to a ControlAction. Every refusal is recorded
/// as a Blocked ControlAction plus an audit event; nothing is dispatched unless every check passes.
/// </summary>
public sealed class AutonomousControlService(
    IControlRepository control,
    IRecommendationRepository recommendations,
    IExecutionRepository executions,
    IConfiguredLogicRepository configuredLogic,
    IEnumerable<IControlCommandSink> sinks,
    AutonomousControlOptions options,
    TimeProvider timeProvider)
{
    public async Task<int> ProcessExecutionAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        var execution = await executions.GetAsync(executionId, cancellationToken);
        if (execution is not { Mode: ExecutionMode.Autonomous, Status: ExecutionStatus.Completed })
        {
            return 0;
        }

        var processed = 0;
        foreach (var recommendation in await recommendations.ListByExecutionAsync(executionId, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (recommendation.Status != RecommendationStatus.Created)
            {
                continue;
            }

            await ProcessAsync(recommendation, cancellationToken);
            processed++;
        }

        return processed;
    }

    public async Task<int> SweepAsync(int limit, CancellationToken cancellationToken = default)
    {
        var total = 0;
        foreach (var id in await control.ListExecutionsWithUndecidedAutonomousRecommendationsAsync(limit, cancellationToken))
        {
            total += await ProcessExecutionAsync(id, cancellationToken);
        }

        return total;
    }

    private async Task ProcessAsync(RecommendationRecord recommendation, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var policy = await control.GetPolicyAsync(recommendation.ConfiguredLogicId, cancellationToken);
        var killSwitch = await control.GetKillSwitchAsync(cancellationToken);
        var definition = await configuredLogic.GetAsync(recommendation.ConfiguredLogicId, cancellationToken);
        var revision = definition?.Revisions.SingleOrDefault(item => item.Id == recommendation.ConfigurationRevisionId);

        var refusal = Evaluate(recommendation, policy, killSwitch, definition, revision, now);
        var sinkKind = policy?.SinkKind ?? ControlSinkKinds.Simulator;
        if (refusal is not null)
        {
            await control.RecordBlockedAsync(recommendation, refusal, sinkKind, now, cancellationToken);
            return;
        }

        var sink = sinks.FirstOrDefault(item =>
            string.Equals(item.Kind, policy!.SinkKind, StringComparison.Ordinal));
        if (sink is null)
        {
            await control.RecordBlockedAsync(recommendation, GateCodes.SinkUnavailable, sinkKind, now, cancellationToken);
            return;
        }

        var (outcome, action) = await control.TryClaimAsync(
            new ControlClaim(recommendation, policy!.SinkKind, policy.MaxActionsPerWindow, policy.WindowSeconds, now),
            cancellationToken);
        if (outcome == ControlClaimOutcome.RateLimited)
        {
            await control.RecordBlockedAsync(recommendation, GateCodes.RateLimited, sinkKind, now, cancellationToken);
            return;
        }

        if (outcome != ControlClaimOutcome.Claimed || action is null)
        {
            return;
        }

        await DispatchClaimedAsync(action, sink, cancellationToken);
    }

    /// <summary>
    /// Approved Recommendation -> governed action. Operator approval is the authority, but the same gate
    /// (kill switch, global flag, per-configuration opt-in, allowlist, bounds, expiry, rate limit) still applies.
    /// Idempotent: a recommendation can only ever produce one ControlAction.
    /// </summary>
    public async Task<ControlExecutionResult> ExecuteApprovedAsync(Guid recommendationId, string actor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl))
            throw Validation("CONTROL_POLICY_ACTOR_INVALID", "An authenticated actor is required.");

        var recommendation = await recommendations.GetAsync(recommendationId, cancellationToken) ??
            throw new WellSiteAutoPilotException("RECOMMENDATION_NOT_FOUND", FailureKind.NotFound,
                "The requested recommendation was not found.");
        if (await control.GetActionByRecommendationAsync(recommendationId, cancellationToken) is { } existing)
            return new ControlExecutionResult(existing, null);

        if (recommendation.Status != RecommendationStatus.Approved || string.IsNullOrWhiteSpace(recommendation.DecisionBy) ||
            recommendation.DecisionBy.StartsWith("system:", StringComparison.Ordinal))
            throw new WellSiteAutoPilotException("CONTROL_RECOMMENDATION_NOT_APPROVED", FailureKind.Conflict,
                "Only an operator-approved recommendation can be executed.");

        var now = timeProvider.GetUtcNow();
        var policy = await control.GetPolicyAsync(recommendation.ConfiguredLogicId, cancellationToken);
        var killSwitch = await control.GetKillSwitchAsync(cancellationToken);
        var definition = await configuredLogic.GetAsync(recommendation.ConfiguredLogicId, cancellationToken);
        var revision = definition?.Revisions.SingleOrDefault(item => item.Id == recommendation.ConfigurationRevisionId);

        var refusal = Evaluate(recommendation, policy, killSwitch, definition, revision, now, ControlOrigins.ApprovedRecommendation);
        var sink = refusal is null
            ? sinks.FirstOrDefault(item => string.Equals(item.Kind, policy!.SinkKind, StringComparison.Ordinal))
            : null;
        refusal ??= sink is null ? GateCodes.SinkUnavailable : null;
        if (refusal is not null)
        {
            await control.RecordRefusalAsync(recommendation, refusal, actor.Trim(), now, cancellationToken);
            return new ControlExecutionResult(null, refusal);
        }

        var (outcome, action) = await control.TryClaimAsync(
            new ControlClaim(recommendation, policy!.SinkKind, policy.MaxActionsPerWindow, policy.WindowSeconds, now,
                ControlOrigins.ApprovedRecommendation), cancellationToken);
        if (outcome == ControlClaimOutcome.RateLimited)
        {
            await control.RecordRefusalAsync(recommendation, GateCodes.RateLimited, actor.Trim(), now, cancellationToken);
            return new ControlExecutionResult(null, GateCodes.RateLimited);
        }

        if (outcome != ControlClaimOutcome.Claimed || action is null)
        {
            var current = await control.GetActionByRecommendationAsync(recommendationId, cancellationToken);
            return new ControlExecutionResult(current, current is null ? GateCodes.Expired : null);
        }

        await DispatchClaimedAsync(action, sink!, cancellationToken);
        return new ControlExecutionResult(await control.GetActionAsync(action.Id, cancellationToken), null);
    }

    private async Task DispatchClaimedAsync(ControlActionRecord action, IControlCommandSink sink,
        CancellationToken cancellationToken)
    {
        // Re-read the kill switch after the claim: it must be able to stop a command that has not left yet.
        if ((await control.GetKillSwitchAsync(cancellationToken)).Engaged)
        {
            await control.CompleteAsync(action.Id, ControlActionStatus.Blocked, GateCodes.KillSwitch, null,
                timeProvider.GetUtcNow(), null, cancellationToken);
            return;
        }

        try
        {
            var result = await sink.DispatchAsync(DispatchRequest(action), cancellationToken);
            await control.CompleteAsync(
                action.Id,
                result.Accepted ? ControlActionStatus.Dispatched : ControlActionStatus.Failed,
                result.Accepted ? GateCodes.Allowed : result.Reference is GateCodes.RealWritesDisabled
                    ? GateCodes.RealWritesDisabled : GateCodes.SinkRefused,
                result.Reference,
                timeProvider.GetUtcNow(),
                null,
                CancellationToken.None);
            if (!result.Accepted) return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The command may or may not have reached the target: never assume Failed.
            await control.CompleteAsync(action.Id, ControlActionStatus.Ambiguous, GateCodes.SinkAmbiguous, null,
                timeProvider.GetUtcNow(), null, CancellationToken.None);
        }

        await VerifyCoreAsync(action.Id, sink, ControlActor.Gate, CancellationToken.None);
    }

    /// <summary>Reads the target back for a Dispatched or Ambiguous action and settles it when the readback is conclusive.</summary>
    public async Task<ControlActionRecord> VerifyAsync(Guid actionId, string actor, CancellationToken cancellationToken = default)
    {
        var action = await control.GetActionAsync(actionId, cancellationToken) ??
            throw new WellSiteAutoPilotException("CONTROL_ACTION_NOT_FOUND", FailureKind.NotFound, "The control action was not found.");
        if (action.Status is not (ControlActionStatus.Dispatched or ControlActionStatus.Ambiguous))
            throw new WellSiteAutoPilotException("CONTROL_ACTION_NOT_VERIFIABLE", FailureKind.Conflict,
                "Only Dispatched or Ambiguous actions can be verified.");
        var sink = sinks.FirstOrDefault(item => string.Equals(item.Kind, action.Sink, StringComparison.Ordinal)) ??
            throw new WellSiteAutoPilotException(GateCodes.SinkUnavailable, FailureKind.Conflict, "The sink is unavailable.");
        await VerifyCoreAsync(actionId, sink, actor.Trim(), cancellationToken);
        return (await control.GetActionAsync(actionId, cancellationToken))!;
    }

    private async Task VerifyCoreAsync(Guid actionId, IControlCommandSink sink, string actor,
        CancellationToken cancellationToken)
    {
        var action = await control.GetActionAsync(actionId, cancellationToken);
        if (action is null || action.Status is not (ControlActionStatus.Dispatched or ControlActionStatus.Ambiguous)) return;

        ControlReadback readback;
        try
        {
            readback = await sink.ReadbackAsync(DispatchRequest(action), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var from = new[] { action.Status };
        switch (readback.State)
        {
            case ControlReadbackState.Applied when readback.ObservedValue == action.Value:
                await control.TransitionAsync(actionId, from, ControlActionStatus.Verified, GateCodes.ReadbackConfirmed,
                    readback.ObservedValue, actor, null, now, cancellationToken);
                break;
            case ControlReadbackState.Applied:
                if (action.Status == ControlActionStatus.Dispatched)
                    await control.TransitionAsync(actionId, from, ControlActionStatus.Ambiguous, GateCodes.ReadbackMismatch,
                        readback.ObservedValue, actor, null, now, cancellationToken);
                break;
            case ControlReadbackState.NotApplied when action.Status == ControlActionStatus.Ambiguous && action.SinkReference is null:
                // The dispatch never reached the target (readback is authoritative by idempotency key).
                await control.TransitionAsync(actionId, from, ControlActionStatus.Failed, GateCodes.ReadbackNotApplied,
                    null, actor, null, now, cancellationToken);
                break;
            case ControlReadbackState.NotApplied when action.Status == ControlActionStatus.Dispatched:
                await control.TransitionAsync(actionId, from, ControlActionStatus.Ambiguous, GateCodes.ReadbackNotApplied,
                    null, actor, null, now, cancellationToken);
                break;
        }
    }

    /// <summary>Operator resolution of an Ambiguous (or unverified Dispatched) action; always audited with a note.</summary>
    public async Task<ControlActionRecord> ResolveAsync(Guid actionId, bool applied, string note, string actor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(note) || note.Length > 1024 || note.Any(char.IsControl))
            throw Validation("CONTROL_RESOLUTION_INVALID", "A resolution note and authenticated actor are required.");
        var resolved = await control.TransitionAsync(actionId,
            [ControlActionStatus.Ambiguous, ControlActionStatus.Dispatched],
            applied ? ControlActionStatus.Verified : ControlActionStatus.Failed,
            applied ? GateCodes.OperatorResolvedApplied : GateCodes.OperatorResolvedNotApplied,
            null, actor.Trim(), note.Trim(), timeProvider.GetUtcNow(), cancellationToken);
        if (resolved is not null) return resolved;
        _ = await control.GetActionAsync(actionId, cancellationToken) ??
            throw new WellSiteAutoPilotException("CONTROL_ACTION_NOT_FOUND", FailureKind.NotFound, "The control action was not found.");
        throw new WellSiteAutoPilotException("CONTROL_ACTION_NOT_RESOLVABLE", FailureKind.Conflict,
            "Only Ambiguous or Dispatched actions can be resolved.");
    }

    private static ControlDispatchRequest DispatchRequest(ControlActionRecord action) =>
        new(action.Id, action.AssetId, action.Command, action.Quantity, action.Value, action.Unit,
            $"control-action:{action.Id:N}");

    internal string? Evaluate(RecommendationRecord recommendation, AutonomyPolicy? policy,
        ControlKillSwitch killSwitch, ConfiguredLogicDefinition? definition, ConfiguredLogicRevision? revision,
        DateTimeOffset now, string origin = ControlOrigins.Autonomous)
    {
        var approvedOrigin = origin == ControlOrigins.ApprovedRecommendation;
        if (killSwitch.Engaged) return GateCodes.KillSwitch;
        if (!options.Enabled) return GateCodes.GlobalDisabled;
        if (policy is null) return GateCodes.NoPolicy;
        if (!policy.Enabled) return GateCodes.PolicyDisabled;
        if (policy.SinkKind is not (ControlSinkKinds.Simulator or ControlSinkKinds.DryRun)) return GateCodes.RealWritesDisabled;
        if (approvedOrigin && !policy.AllowApprovedRecommendations) return GateCodes.ApprovedNotEnabled;
        if (revision is null || revision.Mode != (approvedOrigin ? ExecutionMode.Recommendation : ExecutionMode.Autonomous))
            return GateCodes.NotAutonomous;
        if (definition!.ActiveRevisionId != revision.Id || revision.Status != ConfiguredLogicRevisionStatus.Active)
            return GateCodes.SourceInvalid;
        if (recommendation.ExpiresAtUtc is { } expiry && expiry <= now) return GateCodes.Expired;

        var limit = policy.Limits.FirstOrDefault(item =>
            string.Equals(item.Command, recommendation.Command, StringComparison.Ordinal));
        if (limit is null) return GateCodes.CommandNotPermitted;

        if (recommendation.SuggestedValue is not { } value) return GateCodes.ValueRequired;
        if (limit.Unit is not null && !string.Equals(limit.Unit, recommendation.Unit, StringComparison.Ordinal))
            return GateCodes.UnitMismatch;
        if ((limit.MinimumValue is { } min && value < min) || (limit.MaximumValue is { } max && value > max))
            return GateCodes.OutOfBounds;
        return null;
    }

    public Task<AutonomyPolicy?> GetPolicyAsync(Guid configuredLogicId, CancellationToken cancellationToken = default) =>
        control.GetPolicyAsync(configuredLogicId, cancellationToken);

    public async Task<AutonomyPolicy> SetPolicyAsync(Guid configuredLogicId, bool enabled, string sinkKind,
        IReadOnlyCollection<AutonomyCommandLimit> limits, int maxActionsPerWindow, int windowSeconds,
        string actor, bool allowApprovedRecommendations = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl))
            throw Validation("CONTROL_POLICY_ACTOR_INVALID", "An authenticated actor is required.");
        if (sinkKind is not (ControlSinkKinds.Simulator or ControlSinkKinds.DryRun))
            throw new WellSiteAutoPilotException("CONTROL_REAL_WRITES_DISABLED", FailureKind.Validation,
                "Real provider writes are disabled; only the Simulator and DryRun sinks can be configured.");
        if (maxActionsPerWindow is < 1 or > 1000 || windowSeconds is < 1 or > 86400)
            throw Validation("CONTROL_POLICY_RATE_INVALID", "Rate limit must be 1-1000 actions per 1-86400 seconds.");
        if (limits is null || limits.Count > 64 || limits.Any(item =>
                string.IsNullOrWhiteSpace(item.Command) || item.Command.Length > 256 ||
                (item.MinimumValue > item.MaximumValue) ||
                (enabled && (item.MinimumValue is null || item.MaximumValue is null))))
            throw Validation("CONTROL_POLICY_LIMITS_INVALID",
                "Each permitted command needs a name and, when enabled, inclusive minimum and maximum bounds.");
        if (enabled && limits.Count == 0)
            throw Validation("CONTROL_POLICY_LIMITS_INVALID", "An enabled policy must explicitly permit at least one command.");

        var definition = await configuredLogic.GetAsync(configuredLogicId, cancellationToken) ??
            throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_NOT_FOUND", FailureKind.NotFound,
                "The requested Configured Logic was not found.");

        var policy = new AutonomyPolicy(definition.Id, enabled, sinkKind,
            limits.Select(item => item with { Command = item.Command.Trim() }).ToArray(),
            maxActionsPerWindow, windowSeconds, actor.Trim(), timeProvider.GetUtcNow(), allowApprovedRecommendations);
        await control.UpsertPolicyAsync(policy, cancellationToken);
        return policy;
    }

    public Task<ControlKillSwitch> GetKillSwitchAsync(CancellationToken cancellationToken = default) =>
        control.GetKillSwitchAsync(cancellationToken);

    public async Task<ControlKillSwitch> SetKillSwitchAsync(bool engaged, string actor, string? reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl) ||
            (reason is not null && (reason.Length > 1024 || reason.Any(char.IsControl))))
            throw Validation("CONTROL_KILL_SWITCH_INVALID", "A valid actor and reason are required.");
        await control.SetKillSwitchAsync(engaged, actor.Trim(), reason, timeProvider.GetUtcNow(), cancellationToken);
        return await control.GetKillSwitchAsync(cancellationToken);
    }

    public Task<IReadOnlyCollection<ControlActionRecord>> ListActionsAsync(Guid? configuredLogicId, int limit,
        CancellationToken cancellationToken = default) =>
        control.ListActionsAsync(configuredLogicId, Math.Clamp(limit, 1, 200), cancellationToken);

    private static WellSiteAutoPilotException Validation(string code, string message) =>
        new(code, FailureKind.Validation, message);
}
