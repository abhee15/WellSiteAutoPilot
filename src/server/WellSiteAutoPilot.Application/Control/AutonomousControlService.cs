using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Recommendations;
using WellSiteAutoPilot.Application.Security;
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
    public const string SinkAmbiguous = "SINK_AMBIGUOUS";
    public const string ReadbackConfirmed = "READBACK_CONFIRMED";
    public const string ReadbackMismatch = "READBACK_MISMATCH";
    public const string ReadbackNotApplied = "READBACK_NOT_APPLIED";
    public const string OperatorResolvedApplied = "OPERATOR_RESOLVED_APPLIED";
    public const string OperatorResolvedNotApplied = "OPERATOR_RESOLVED_NOT_APPLIED";
    public const string ApprovedNotEnabled = "GATE_APPROVED_EXECUTION_NOT_ENABLED";
    public const string NotRecommendationMode = "GATE_NOT_RECOMMENDATION_MODE";
    public const string Stale = "GATE_RECOMMENDATION_STALE";
    public const string AdvisoryNotExecutable = "GATE_ADVISORY_NOT_EXECUTABLE";
    public const string TargetNoConnection = "GATE_TARGET_NO_CONNECTION";
    public const string TargetConnectionDisabled = "GATE_TARGET_CONNECTION_DISABLED";
    public const string TargetNotBound = "GATE_TARGET_NOT_BOUND";
    public const string TargetAmbiguous = "GATE_TARGET_AMBIGUOUS";
    public const string CommandNotInCatalog = "GATE_COMMAND_NOT_IN_CATALOG";
    public const string CommandNotEnabled = "GATE_COMMAND_NOT_ENABLED";
    public const string CatalogOutOfBounds = "GATE_CATALOG_OUT_OF_BOUNDS";
}

/// <summary>
/// Outcome of executing an approved recommendation: the action (new or already existing) or the gate code that
/// refused it. A refusal leaves the recommendation Approved and writes only an audit event.
/// </summary>
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
    TimeProvider timeProvider,
    IControlTargetResolver targetResolver)
{
    private const string ReadbackActor = "system:control-readback";

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

        var (target, targetRefusal) = await ResolveTargetAsync(recommendation, policy!, revision!, cancellationToken);
        if (targetRefusal is not null)
        {
            await control.RecordBlockedAsync(recommendation, targetRefusal, sinkKind, now, cancellationToken);
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

        await DispatchClaimedAsync(action, sink, target, cancellationToken);
    }

    /// <summary>
    /// Approved Recommendation -> governed action. A person's approval is the authority, but the same gate still
    /// applies (kill switch, deployment switch, per-configuration opt-in, allowlist, bounds, expiry, rate limit).
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
        // A repeat returns the action this approval already produced. An action the autonomy gate produced is not
        // an execution of an approval, so it falls through to the approval check below and is refused.
        if (await control.GetActionByRecommendationAsync(recommendationId, cancellationToken) is
            { Origin: ControlOrigins.ApprovedRecommendation } existing)
            return new ControlExecutionResult(existing, null);

        // A person must have approved it: the gate's own Autonomous decisions never qualify.
        if (recommendation.Status != RecommendationStatus.Approved || string.IsNullOrWhiteSpace(recommendation.DecisionBy) ||
            recommendation.DecisionBy.StartsWith("system:", StringComparison.Ordinal))
            throw new WellSiteAutoPilotException("CONTROL_RECOMMENDATION_NOT_APPROVED", FailureKind.Conflict,
                "Only a recommendation approved by a person can be executed.");

        var now = timeProvider.GetUtcNow();
        var policy = await control.GetPolicyAsync(recommendation.ConfiguredLogicId, cancellationToken);
        var killSwitch = await control.GetKillSwitchAsync(cancellationToken);
        var definition = await configuredLogic.GetAsync(recommendation.ConfiguredLogicId, cancellationToken);
        var revision = definition?.Revisions.SingleOrDefault(item => item.Id == recommendation.ConfigurationRevisionId);

        var refusal = Evaluate(recommendation, policy, killSwitch, definition, revision, now,
            ControlOrigins.ApprovedRecommendation);
        var sink = refusal is null
            ? sinks.FirstOrDefault(item => string.Equals(item.Kind, policy!.SinkKind, StringComparison.Ordinal))
            : null;
        refusal ??= sink is null ? GateCodes.SinkUnavailable : null;
        if (refusal is not null)
        {
            await control.RecordRefusalAsync(recommendation, refusal, actor.Trim(), now, cancellationToken);
            return new ControlExecutionResult(null, refusal);
        }

        var (target, targetRefusal) = await ResolveTargetAsync(recommendation, policy!, revision!, cancellationToken);
        if (targetRefusal is not null)
        {
            await control.RecordRefusalAsync(recommendation, targetRefusal, actor.Trim(), now, cancellationToken);
            return new ControlExecutionResult(null, targetRefusal);
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
            // Lost a race with another execution of the same recommendation, or it is no longer eligible.
            var current = await control.GetActionByRecommendationAsync(recommendationId, cancellationToken);
            return new ControlExecutionResult(current, current is null ? GateCodes.Expired : null);
        }

        await DispatchClaimedAsync(action, sink!, target, cancellationToken);
        return new ControlExecutionResult(await control.GetActionAsync(action.Id, cancellationToken), null);
    }

    /// <summary>
    /// For the Provider sink: where the command would go, and whether the site's catalog and the allowed adapters let
    /// it go there. Nothing here applies to the Simulator sink, which never touches a connection. Every doubt is a gate
    /// code.
    /// </summary>
    private async Task<(ControlTarget? Target, string? Refusal)> ResolveTargetAsync(
        RecommendationRecord recommendation, AutonomyPolicy policy, ConfiguredLogicRevision revision,
        CancellationToken cancellationToken)
    {
        if (policy.SinkKind != ControlSinkKinds.Provider) return (null, null);
        var resolution = await targetResolver.ResolveAsync(
            recommendation.AssetId, recommendation.Command, revision.DataBindings, cancellationToken);
        if (resolution.Target is not { } target) return (null, resolution.RefusalCode ?? GateCodes.TargetNotBound);
        if (!options.ProviderAdapterAllowed(target.AdapterKey)) return (null, GateCodes.RealWritesDisabled);
        // Checked again here, not only when the policy was saved, so a catalog narrowed later also stops an older policy.
        var catalogRefusal = recommendation.SuggestedValue is { } value
            ? ControlTargetRules.CheckCatalog(target.Entry, value)
            : GateCodes.ValueRequired;
        return catalogRefusal is null ? (target, null) : (null, catalogRefusal);
    }

    /// <summary>
    /// The target of an action that already exists, resolved from today's connection and catalog. Null when it can no
    /// longer be found, in which case the target cannot be asked and nothing is guessed.
    /// </summary>
    private async Task<ControlTarget?> ResolveForActionAsync(ControlActionRecord action, CancellationToken cancellationToken)
    {
        var recommendation = await recommendations.GetAsync(action.RecommendationId, cancellationToken);
        var definition = recommendation is null ? null : await configuredLogic.GetAsync(recommendation.ConfiguredLogicId, cancellationToken);
        var revision = definition?.Revisions.SingleOrDefault(item => item.Id == recommendation!.ConfigurationRevisionId);
        if (revision is null) return null;
        return (await targetResolver.ResolveAsync(action.AssetId, action.Command, revision.DataBindings, cancellationToken)).Target;
    }

    private async Task<ControlDispatchRequest?> RequestFor(ControlActionRecord action, ControlTarget? target,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(action.Sink, ControlSinkKinds.Provider, StringComparison.Ordinal)) return DispatchRequest(action, null);
        target ??= await ResolveForActionAsync(action, cancellationToken);
        return target is null ? null : DispatchRequest(action, target);
    }

    private async Task DispatchClaimedAsync(ControlActionRecord action, IControlCommandSink sink, ControlTarget? target,
        CancellationToken cancellationToken)
    {
        // Re-read the kill switch after the claim: it must be able to stop a command that has not left yet.
        if ((await control.GetKillSwitchAsync(cancellationToken)).Engaged)
        {
            await control.CompleteAsync(action.Id, ControlActionStatus.Blocked, GateCodes.KillSwitch, null,
                timeProvider.GetUtcNow(), cancellationToken);
            return;
        }

        // Built before anything is sent: a command with nowhere to go is blocked, not reported as ambiguous.
        var request = await RequestFor(action, target, cancellationToken);
        if (request is null)
        {
            await control.CompleteAsync(action.Id, ControlActionStatus.Blocked, GateCodes.TargetNotBound, null,
                timeProvider.GetUtcNow(), CancellationToken.None);
            return;
        }

        ControlDispatchResult result;
        try
        {
            result = await sink.DispatchAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The command may or may not have reached the device, so never assume it failed: that is for readback
            // or an operator to settle.
            await control.CompleteAsync(action.Id, ControlActionStatus.Ambiguous, GateCodes.SinkAmbiguous, null,
                timeProvider.GetUtcNow(), CancellationToken.None);
            return;
        }
        catch (OperationCanceledException)
        {
            await control.CompleteAsync(action.Id, ControlActionStatus.Ambiguous, GateCodes.SinkAmbiguous, null,
                timeProvider.GetUtcNow(), CancellationToken.None);
            throw;
        }

        if (!result.Accepted)
        {
            await control.CompleteAsync(action.Id, ControlActionStatus.Failed,
                result.Reference is GateCodes.RealWritesDisabled ? GateCodes.RealWritesDisabled : GateCodes.SinkRefused,
                result.Reference, timeProvider.GetUtcNow(), CancellationToken.None);
            return;
        }

        await control.CompleteAsync(action.Id, ControlActionStatus.Dispatched, GateCodes.Allowed, result.Reference,
            timeProvider.GetUtcNow(), CancellationToken.None);
        if (await control.GetActionAsync(action.Id, CancellationToken.None) is { } dispatched)
        {
            try
            {
                await VerifyCoreAsync(dispatched, sink, ReadbackActor, CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The target could not be asked: the action stays Dispatched and can be verified again later.
            }
        }
    }

    /// <summary>
    /// Asks the sink what the target holds now. A matching value verifies the action; a different value or a
    /// dispatched command the target does not show is ambiguous; an unanswerable readback changes nothing.
    /// </summary>
    public async Task<ControlActionRecord> VerifyAsync(Guid actionId, string actor,
        CancellationToken cancellationToken = default)
    {
        RequireActor(actor);
        var action = await control.GetActionAsync(actionId, cancellationToken) ?? throw ActionNotFound();
        if (action.Status is not (ControlActionStatus.Dispatched or ControlActionStatus.Ambiguous))
            throw new WellSiteAutoPilotException("CONTROL_ACTION_NOT_VERIFIABLE", FailureKind.Conflict,
                "Only a dispatched or ambiguous action can be verified.");
        var sink = sinks.FirstOrDefault(item => string.Equals(item.Kind, action.Sink, StringComparison.Ordinal)) ??
            throw new WellSiteAutoPilotException("CONTROL_SINK_UNAVAILABLE", FailureKind.Conflict,
                "The sink that carried this action is not available.");
        return await VerifyCoreAsync(action, sink, actor.Trim(), cancellationToken);
    }

    /// <summary>
    /// A person settles an action nobody can verify automatically. The attestation and note are audited, and the
    /// action says so with an operator gate code rather than pretending a readback happened.
    /// </summary>
    public async Task<ControlActionRecord> ResolveAsync(Guid actionId, bool applied, string note, string actor,
        CancellationToken cancellationToken = default)
    {
        RequireActor(actor);
        if (string.IsNullOrWhiteSpace(note) || note.Trim().Length > 1024 ||
            note.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t')))
            throw Validation("CONTROL_RESOLUTION_NOTE_INVALID",
                "A resolution note of up to 1024 characters is required.");
        _ = await control.GetActionAsync(actionId, cancellationToken) ?? throw ActionNotFound();
        return await control.TransitionAsync(actionId,
                   [ControlActionStatus.Ambiguous, ControlActionStatus.Dispatched],
                   applied ? ControlActionStatus.Verified : ControlActionStatus.Failed,
                   applied ? GateCodes.OperatorResolvedApplied : GateCodes.OperatorResolvedNotApplied,
                   null, actor.Trim(), note.Trim(), timeProvider.GetUtcNow(), cancellationToken) ??
               throw new WellSiteAutoPilotException("CONTROL_ACTION_NOT_RESOLVABLE", FailureKind.Conflict,
                   "Only a dispatched or ambiguous action can be resolved, and it may already have been settled.");
    }

    public Task<bool> CanDecideActionAsync(AccessSubject subject, Guid actionId,
        CancellationToken cancellationToken = default) =>
        control.CanDecideActionAsync(subject, actionId, cancellationToken);

    private async Task<ControlActionRecord> VerifyCoreAsync(ControlActionRecord action, IControlCommandSink sink,
        string actor, CancellationToken cancellationToken)
    {
        // A provider action is asked about at its target as it is now. Without one nothing can be asked, and the action
        // stays as it is for a person to resolve.
        if (await RequestFor(action, null, cancellationToken) is not { } request) return action;
        var readback = await sink.ReadbackAsync(request, cancellationToken);
        var (target, permitted, code) = readback.State switch
        {
            // Sinks report the observed value in the commanded unit; equality is exact because a setpoint that
            // reads back differently has not been applied as asked.
            ControlReadbackState.Applied when action.Value is null || readback.ObservedValue == action.Value =>
                (ControlActionStatus.Verified, new[] { ControlActionStatus.Dispatched, ControlActionStatus.Ambiguous },
                    GateCodes.ReadbackConfirmed),
            ControlReadbackState.Applied when action.Status == ControlActionStatus.Dispatched =>
                (ControlActionStatus.Ambiguous, new[] { ControlActionStatus.Dispatched }, GateCodes.ReadbackMismatch),
            ControlReadbackState.NotApplied when action.Status == ControlActionStatus.Dispatched =>
                (ControlActionStatus.Ambiguous, new[] { ControlActionStatus.Dispatched }, GateCodes.ReadbackNotApplied),
            // Never accepted by the sink and not on the target: the command did not happen.
            ControlReadbackState.NotApplied when action.Status == ControlActionStatus.Ambiguous &&
                                                 action.SinkReference is null =>
                (ControlActionStatus.Failed, new[] { ControlActionStatus.Ambiguous }, GateCodes.ReadbackNotApplied),
            _ => (action.Status, Array.Empty<ControlActionStatus>(), action.GateCode)
        };
        if (permitted.Length == 0) return action;
        return await control.TransitionAsync(action.Id, permitted, target, code, readback.ObservedValue, actor, null,
                   timeProvider.GetUtcNow(), cancellationToken) ??
               await control.GetActionAsync(action.Id, cancellationToken) ?? action;
    }

    private static ControlDispatchRequest DispatchRequest(ControlActionRecord action, ControlTarget? target) =>
        new(action.Id, action.AssetId, action.Command, action.Quantity, action.Value, action.Unit,
            $"control-action:{action.Id:N}", target);

    private static void RequireActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl))
            throw Validation("CONTROL_POLICY_ACTOR_INVALID", "An authenticated actor is required.");
    }

    private static WellSiteAutoPilotException ActionNotFound() =>
        new("CONTROL_ACTION_NOT_FOUND", FailureKind.NotFound, "The requested control action was not found.");

    internal string? Evaluate(RecommendationRecord recommendation, AutonomyPolicy? policy,
        ControlKillSwitch killSwitch, ConfiguredLogicDefinition? definition, ConfiguredLogicRevision? revision,
        DateTimeOffset now, string origin = ControlOrigins.Autonomous)
    {
        var approvedOrigin = origin == ControlOrigins.ApprovedRecommendation;
        // Advice has no device action behind it, however a person decided it.
        if (recommendation.Command == RecommendationCommands.Advisory) return GateCodes.AdvisoryNotExecutable;
        if (killSwitch.Engaged) return GateCodes.KillSwitch;
        if (!options.Enabled) return GateCodes.GlobalDisabled;
        if (policy is null) return GateCodes.NoPolicy;
        if (!policy.Enabled) return GateCodes.PolicyDisabled;
        if (policy.SinkKind != ControlSinkKinds.Simulator &&
            (policy.SinkKind != ControlSinkKinds.Provider || options.AllowedProviderAdapters.Count == 0))
            return GateCodes.RealWritesDisabled;
        if (approvedOrigin && !policy.AllowApprovedRecommendations) return GateCodes.ApprovedNotEnabled;
        if (revision is null) return approvedOrigin ? GateCodes.NotRecommendationMode : GateCodes.NotAutonomous;
        if (revision.Mode != (approvedOrigin ? ExecutionMode.Recommendation : ExecutionMode.Autonomous))
            return approvedOrigin ? GateCodes.NotRecommendationMode : GateCodes.NotAutonomous;
        if (definition!.ActiveRevisionId != revision.Id || revision.Status != ConfiguredLogicRevisionStatus.Active)
            return GateCodes.SourceInvalid;
        if (recommendation.ExpiresAtUtc is { } expiry && expiry <= now) return GateCodes.Expired;
        if (approvedOrigin && now - recommendation.CreatedAtUtc > options.ApprovedRecommendationMaxAge)
            return GateCodes.Stale;

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
        string actor, bool allowApprovedRecommendations = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl))
            throw Validation("CONTROL_POLICY_ACTOR_INVALID", "An authenticated actor is required.");
        if (sinkKind != ControlSinkKinds.Simulator &&
            (sinkKind != ControlSinkKinds.Provider || options.AllowedProviderAdapters.Count == 0))
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
        if (limits.Any(item => string.Equals(item.Command.Trim(), RecommendationCommands.Advisory, StringComparison.OrdinalIgnoreCase)))
            throw Validation("CONTROL_POLICY_LIMITS_INVALID", "The advisory command is reserved and cannot be permitted.");
        // The gate takes the first matching entry, so a second entry for the same command would never apply and would
        // read as a rule that does. Names differing only by case are the same command to a person, so reject them too.
        if (limits.Select(item => item.Command.Trim()).GroupBy(name => name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw Validation("CONTROL_POLICY_LIMITS_INVALID", "Each command can be permitted only once.");
        if (enabled && limits.Count == 0)
            throw Validation("CONTROL_POLICY_LIMITS_INVALID", "An enabled policy must explicitly permit at least one command.");

        var definition = await configuredLogic.GetAsync(configuredLogicId, cancellationToken) ??
            throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_NOT_FOUND", FailureKind.NotFound,
                "The requested Configured Logic was not found.");

        // A policy can only permit what the live revision's module can ask for. With no live revision nothing can run,
        // so there is nothing to judge against yet; the check happens again when a policy is next saved.
        if (definition.Revisions.FirstOrDefault(item => item.Id == definition.ActiveRevisionId) is { } active && limits.Count > 0)
        {
            var declared = ModuleCommandDeclarations.Read(active.ModuleManifestJson) ??
                throw Validation("CONTROL_POLICY_MODULE_UNREADABLE",
                    "The live revision's module manifest could not be read, so its commands cannot be checked.");
            var problems = ModuleCommandDeclarations.Check(limits.Select(item => item with { Command = item.Command.Trim() }).ToArray(), declared);
            if (problems.Count > 0)
                throw Validation("CONTROL_POLICY_COMMAND_NOT_DECLARED", string.Join(" ", problems.Take(5)));
        }

        if (sinkKind == ControlSinkKinds.Provider && enabled)
            await RequireProviderTargetsAsync(definition, limits, cancellationToken);

        var policy = new AutonomyPolicy(definition.Id, enabled, sinkKind,
            limits.Select(item => item with { Command = item.Command.Trim() }).ToArray(),
            maxActionsPerWindow, windowSeconds, actor.Trim(), timeProvider.GetUtcNow(), allowApprovedRecommendations);
        await control.UpsertPolicyAsync(policy, cancellationToken);
        return policy;
    }

    /// <summary>
    /// A Provider policy must name only commands that every asset the logic touches can actually receive: each target
    /// resolves, sits on an allowed adapter, and the policy's limits stay inside the site's catalog. A policy can
    /// narrow what the site allows but never widen it. The gate checks the catalog again at dispatch.
    /// </summary>
    private async Task RequireProviderTargetsAsync(ConfiguredLogicDefinition definition,
        IReadOnlyCollection<AutonomyCommandLimit> limits, CancellationToken cancellationToken)
    {
        var active = definition.Revisions.FirstOrDefault(item => item.Id == definition.ActiveRevisionId);
        if (active is null)
            throw Validation("CONTROL_POLICY_NO_LIVE_REVISION",
                "A provider policy needs a live revision, so its targets can be checked against the site's command catalog.");
        foreach (var assetId in active.AssetBindings.Select(item => item.AssetId).Distinct())
        {
            foreach (var limit in limits)
            {
                var command = limit.Command.Trim();
                var resolution = await targetResolver.ResolveAsync(assetId, command, active.DataBindings, cancellationToken);
                if (resolution.Target is not { } target)
                    throw Validation("CONTROL_POLICY_TARGET_UNRESOLVED",
                        $"{command} cannot be sent to an asset this logic uses ({resolution.RefusalCode}).");
                if (!options.ProviderAdapterAllowed(target.AdapterKey))
                    throw new WellSiteAutoPilotException("CONTROL_REAL_WRITES_DISABLED", FailureKind.Validation,
                        $"The {target.AdapterKey} adapter is not allowed to receive commands.");
                if (ControlTargetRules.CheckPolicyLimit(limit, target.Entry) is { } refusal)
                    throw Validation("CONTROL_POLICY_EXCEEDS_CATALOG",
                        $"{command} is not allowed on this connection within these limits ({refusal}).");
            }
        }
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
        Guid? configuredLogicId, IReadOnlyCollection<ControlActionStatus>? statuses, bool oldestFirst, int limit,
        CancellationToken cancellationToken = default) =>
        control.ListActionsAuthorizedAsync(subject, configuredLogicId, statuses, oldestFirst, Math.Clamp(limit, 1, 200),
            cancellationToken);

    private static WellSiteAutoPilotException Validation(string code, string message) =>
        new(code, FailureKind.Validation, message);
}
