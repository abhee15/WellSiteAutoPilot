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
}

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

        // Re-read the kill switch after the claim: it must be able to stop a command that has not left yet.
        if ((await control.GetKillSwitchAsync(cancellationToken)).Engaged)
        {
            await control.CompleteAsync(action.Id, ControlActionStatus.Blocked, GateCodes.KillSwitch, null,
                timeProvider.GetUtcNow(), cancellationToken);
            return;
        }

        try
        {
            var result = await sink.DispatchAsync(
                new ControlDispatchRequest(action.Id, action.AssetId, action.Command, action.Quantity,
                    action.Value, action.Unit, $"control-action:{action.Id:N}"),
                cancellationToken);
            await control.CompleteAsync(
                action.Id,
                result.Accepted ? ControlActionStatus.Dispatched : ControlActionStatus.Failed,
                result.Accepted ? GateCodes.Allowed : result.Reference is GateCodes.RealWritesDisabled
                    ? GateCodes.RealWritesDisabled : GateCodes.SinkRefused,
                result.Reference,
                timeProvider.GetUtcNow(),
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await control.CompleteAsync(action.Id, ControlActionStatus.Failed, GateCodes.SinkFailed, null,
                timeProvider.GetUtcNow(), CancellationToken.None);
        }
    }

    internal string? Evaluate(RecommendationRecord recommendation, AutonomyPolicy? policy,
        ControlKillSwitch killSwitch, ConfiguredLogicDefinition? definition, ConfiguredLogicRevision? revision,
        DateTimeOffset now)
    {
        if (killSwitch.Engaged) return GateCodes.KillSwitch;
        if (!options.Enabled) return GateCodes.GlobalDisabled;
        if (policy is null) return GateCodes.NoPolicy;
        if (!policy.Enabled) return GateCodes.PolicyDisabled;
        if (policy.SinkKind != ControlSinkKinds.Simulator) return GateCodes.RealWritesDisabled;
        if (revision is null || revision.Mode != ExecutionMode.Autonomous) return GateCodes.NotAutonomous;
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

    public Task<AutonomyPolicy?> GetPolicyAsync(AccessSubject subject, Guid configuredLogicId,
        CancellationToken cancellationToken = default) =>
        control.GetPolicyAuthorizedAsync(subject, configuredLogicId, cancellationToken);

    public async Task<AutonomyPolicy> SetPolicyAsync(Guid configuredLogicId, bool enabled, string sinkKind,
        IReadOnlyCollection<AutonomyCommandLimit> limits, int maxActionsPerWindow, int windowSeconds,
        string actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl))
            throw Validation("CONTROL_POLICY_ACTOR_INVALID", "An authenticated actor is required.");
        if (sinkKind != ControlSinkKinds.Simulator)
            throw new WellSiteAutoPilotException("CONTROL_REAL_WRITES_DISABLED", FailureKind.Validation,
                "Real provider writes are disabled; only the Simulator sink can be configured.");
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
            maxActionsPerWindow, windowSeconds, actor.Trim(), timeProvider.GetUtcNow());
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

    public Task<IReadOnlyCollection<ControlActionRecord>> ListActionsAsync(AccessSubject subject,
        Guid? configuredLogicId, int limit, CancellationToken cancellationToken = default) =>
        control.ListActionsAuthorizedAsync(subject, configuredLogicId, Math.Clamp(limit, 1, 200), cancellationToken);

    private static WellSiteAutoPilotException Validation(string code, string message) =>
        new(code, FailureKind.Validation, message);
}
