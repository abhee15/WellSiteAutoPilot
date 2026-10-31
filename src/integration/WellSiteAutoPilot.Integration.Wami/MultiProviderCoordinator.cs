using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.Wami;

public enum DependencyCapability
{
    CurrentData = 0,
    ModelArtifact = 1,
    Calculation = 2
}

/// <summary>What a module needs, by role. Which saved connection fills the role is decided separately, per deployment.</summary>
public sealed record ProviderDependency(string Role, DependencyCapability Capability, string AdapterKey);

/// <summary>A role bound to one saved connection at one revision, for one logical asset.</summary>
public sealed record DependencyBinding(
    string Role, string AdapterKey, string ConnectionId, int ConnectionRevision, Guid LogicalAssetId, string? ExternalAssetId);

/// <summary>One calculation input, read from a data role and handed to WAMI under its own name and unit.</summary>
public sealed record CalculationInput(string Role, string Quantity, string WamiInput, string Unit);

public sealed record CompositionPlan(
    string PlanId,
    IReadOnlyList<ProviderDependency> Dependencies,
    IReadOnlyList<DependencyBinding> Bindings,
    string ModelRole,
    string CalculationRole,
    string Function,
    IReadOnlyList<CalculationInput> Inputs,
    TimeSpan CalculationTimeout,
    TimeSpan MaximumInputAge,
    int MaximumBusyAttempts = 2,
    TimeSpan? BusyRetryDelay = null);

/// <summary>What a run saw. Store it with the result; pass it back as the expectation to detect a changed model or binding.</summary>
public sealed record CompositionPins(IReadOnlyList<DependencyBinding> Bindings, ModelArtifactRef Model);

public enum CompositionStatus
{
    Completed = 0,
    /// <summary>Stopped with a definite reason; nothing downstream may use partial output.</summary>
    Failed = 1,
    /// <summary>The calculation may have run and its answer is unknown.</summary>
    Ambiguous = 2,
    /// <summary>The caller cancelled before the calculation was dispatched.</summary>
    Cancelled = 3
}

public sealed record CompositionAuditEvent(DateTimeOffset AtUtc, string Step, string? Role, string Outcome, string Detail);

public sealed record CompositionResult(
    CompositionStatus Status,
    string? FailureCode,
    IReadOnlyList<EngineeringValue> Outputs,
    CompositionPins? Observed,
    IReadOnlyList<CompositionAuditEvent> Audit);

public interface IDependencyAuthorizer
{
    /// <summary>Asked once per role, independently: permission on one provider says nothing about another.</summary>
    ValueTask<bool> IsAllowedAsync(DependencyBinding binding, DependencyCapability capability, CancellationToken cancellationToken);
}

/// <summary>The provider instances for saved connections. Modules never see this; only the host does provider I/O.</summary>
public sealed class ProviderRegistry
{
    private readonly Dictionary<(string Connection, DependencyCapability Capability), object> _providers = [];

    public ProviderRegistry AddCurrentData(string connectionId, ICurrentDataProvider provider) =>
        Add(connectionId, DependencyCapability.CurrentData, provider);

    public ProviderRegistry AddModelSource(string connectionId, IModelArtifactSource provider) =>
        Add(connectionId, DependencyCapability.ModelArtifact, provider);

    public ProviderRegistry AddCalculation(string connectionId, IWamiClient provider) =>
        Add(connectionId, DependencyCapability.Calculation, provider);

    internal T? Find<T>(string connectionId, DependencyCapability capability) where T : class =>
        _providers.GetValueOrDefault((connectionId, capability)) as T;

    private ProviderRegistry Add(string connectionId, DependencyCapability capability, object provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentNullException.ThrowIfNull(provider);
        _providers[(connectionId, capability)] = provider;
        return this;
    }
}

/// <summary>
/// Runs one multi-provider scenario: real-time data from one role, the asset's model from another, a calculation on a third.
/// Every step is checked against the plan's bindings, so changed bindings, a changed model or a denied role stop the run
/// before WAMI is called. This is a proof of composition, not a workflow engine: one fixed sequence, no branching.
/// </summary>
public sealed class MultiProviderCoordinator(ProviderRegistry registry, IDependencyAuthorizer authorizer, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<CompositionResult> RunAsync(
        CompositionPlan plan, CompositionPins? expected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var audit = new List<CompositionAuditEvent>();
        void Log(string step, string? role, string outcome, string detail) =>
            audit.Add(new CompositionAuditEvent(_clock.GetUtcNow(), step, role, outcome, detail));
        CompositionResult Fail(string code, string step, string? role, string detail, CompositionPins? observed = null)
        {
            Log(step, role, code, detail);
            return new CompositionResult(CompositionStatus.Failed, code, [], observed, audit);
        }

        var bindings = ValidateBindings(plan, expected, out var invalid);
        if (invalid is not null) return Fail(invalid, "validate", null, "The bindings do not match the plan or the pinned run.");
        Log("validate", null, "ok", $"{plan.Dependencies.Count} dependencies bound");

        foreach (var dependency in plan.Dependencies)
        {
            var binding = bindings[dependency.Role];
            if (!await authorizer.IsAllowedAsync(binding, dependency.Capability, cancellationToken))
                return Fail("DEPENDENCY_DENIED", "authorize", dependency.Role, $"Not permitted on connection {binding.ConnectionId}.");
        }
        Log("authorize", null, "ok", "every role permitted");

        var calcBinding = bindings[plan.CalculationRole];
        var modelBinding = bindings[plan.ModelRole];
        var wami = registry.Find<IWamiClient>(calcBinding.ConnectionId, DependencyCapability.Calculation);
        var modelSource = registry.Find<IModelArtifactSource>(modelBinding.ConnectionId, DependencyCapability.ModelArtifact);
        if (wami is null || modelSource is null)
            return Fail("DEPENDENCY_UNAVAILABLE", "resolve", null, "No provider is registered for a bound connection.");
        if (wami.Functions.All(function => function.Name != plan.Function))
            return Fail(WamiFailureCodes.FunctionUnsupported, "resolve", plan.CalculationRole, $"'{plan.Function}' is not offered by this WAMI.");

        var inputs = new Dictionary<string, WamiQuantity>(StringComparer.Ordinal);
        var now = _clock.GetUtcNow();
        foreach (var input in plan.Inputs)
        {
            var dataBinding = bindings[input.Role];
            var provider = registry.Find<ICurrentDataProvider>(dataBinding.ConnectionId, DependencyCapability.CurrentData);
            if (provider is null || dataBinding.ExternalAssetId is null)
                return Fail("DEPENDENCY_UNAVAILABLE", "read", input.Role, "The data provider is not available for this asset.");
            EngineeringValue value;
            try { value = await provider.GetCurrentAsync(dataBinding.ExternalAssetId, input.Quantity, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Cancelled(); }
            catch (Exception error) when (error is InvalidOperationException or HttpRequestException or ArgumentException)
            {
                return Fail("INPUT_UNAVAILABLE", "read", input.Role, $"{input.Quantity}: {error.Message}");
            }

            var problem = value switch
            {
                { Value: null } => "INPUT_MISSING",
                { Quality: not "Good" } => "INPUT_QUALITY",
                _ when value.Unit != input.Unit => "INPUT_UNIT",
                _ when now - value.TimestampUtc > plan.MaximumInputAge => "INPUT_STALE",
                _ => null
            };
            if (problem is not null) return Fail(problem, "read", input.Role, $"{input.Quantity} is not usable ({problem}).");
            inputs[input.WamiInput] = new WamiQuantity(value.Value!.Value, value.Unit);
            Log("read", input.Role, "ok", $"{input.Quantity} at {value.TimestampUtc:O}, source {value.Source}");
        }

        ModelArtifact model;
        try { model = await modelSource.GetCurrentModelAsync(modelBinding.ExternalAssetId ?? "", cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Cancelled(); }
        catch (Exception error) when (error is ModelSourceException or ArgumentException or HttpRequestException)
        {
            return Fail(error is ModelSourceException source ? source.Code : "MODEL_SOURCE_FAILED", "model", plan.ModelRole, error.Message);
        }

        var observed = new CompositionPins([.. plan.Bindings], model.Ref);
        if (expected is not null && expected.Model != model.Ref)
            return Fail("MODEL_PIN_MISMATCH", "model", plan.ModelRole, $"Model is {model.Ref.Sha256}, the run was pinned to {expected.Model.Sha256}.", observed);
        Log("model", plan.ModelRole, "ok", $"{model.Ref.ModelId} revision {model.Ref.Revision} sha256 {model.Ref.Sha256}");

        var (result, status, code) = await CalculateAsync(wami, model, plan, inputs, Log, cancellationToken);
        if (result is null)
            return new CompositionResult(status, code, [], observed, audit);

        var stamp = _clock.GetUtcNow();
        var source2 = $"wami:{calcBinding.ConnectionId}@{calcBinding.ConnectionRevision}:{plan.Function}:{model.Ref.Sha256[..12]}";
        var outputs = result.Outputs
            .OrderBy(output => output.Key, StringComparer.Ordinal)
            .Select(output => new EngineeringValue(output.Key, output.Value.Value, output.Value.Unit, stamp, "Good", source2, stamp))
            .ToArray();
        Log("complete", plan.CalculationRole, "ok", $"{outputs.Length} outputs, server {result.ServerVersion}");
        return new CompositionResult(CompositionStatus.Completed, null, outputs, observed, audit);

        CompositionResult Cancelled()
        {
            Log("cancel", null, "cancelled", "Cancelled before the calculation was dispatched.");
            return new CompositionResult(CompositionStatus.Cancelled, null, [], null, audit);
        }
    }

    private static Dictionary<string, DependencyBinding> ValidateBindings(
        CompositionPlan plan, CompositionPins? expected, out string? failure)
    {
        failure = null;
        var byRole = new Dictionary<string, DependencyBinding>(StringComparer.Ordinal);
        foreach (var binding in plan.Bindings)
        {
            if (!byRole.TryAdd(binding.Role, binding)) failure = "DEPENDENCY_SET_INVALID";
        }

        var roles = plan.Dependencies.Select(dependency => dependency.Role).ToHashSet(StringComparer.Ordinal);
        if (roles.Count != plan.Dependencies.Count || !roles.SetEquals(byRole.Keys)) failure ??= "DEPENDENCY_SET_INVALID";
        foreach (var dependency in plan.Dependencies)
        {
            if (byRole.TryGetValue(dependency.Role, out var binding) &&
                !string.Equals(binding.AdapterKey, dependency.AdapterKey, StringComparison.OrdinalIgnoreCase))
                failure ??= "DEPENDENCY_ADAPTER_MISMATCH";
        }

        var capabilities = plan.Dependencies.ToDictionary(dependency => dependency.Role, dependency => dependency.Capability, StringComparer.Ordinal);
        if (!capabilities.TryGetValue(plan.ModelRole, out var modelCap) || modelCap != DependencyCapability.ModelArtifact ||
            !capabilities.TryGetValue(plan.CalculationRole, out var calcCap) || calcCap != DependencyCapability.Calculation ||
            plan.Inputs.Any(input => !capabilities.TryGetValue(input.Role, out var cap) || cap != DependencyCapability.CurrentData))
            failure ??= "DEPENDENCY_SET_INVALID";

        // Distinct sources, one logical asset: matching external ids across connections never establish identity.
        if (byRole.Values.Select(binding => binding.LogicalAssetId).Distinct().Count() > 1) failure ??= "DEPENDENCY_ASSET_MISMATCH";
        if (expected is not null && !expected.Bindings.OrderBy(b => b.Role, StringComparer.Ordinal)
                .SequenceEqual(byRole.Values.OrderBy(b => b.Role, StringComparer.Ordinal)))
            failure ??= "DEPENDENCY_PIN_MISMATCH";
        return byRole;
    }

    private async Task<(WamiCalculationResult? Result, CompositionStatus Status, string? Code)> CalculateAsync(
        IWamiClient wami, ModelArtifact model, CompositionPlan plan, Dictionary<string, WamiQuantity> inputs,
        Action<string, string?, string, string> log, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        for (var attempt = 1; ; attempt++)
        {
            using var deadline = new CancellationTokenSource(plan.CalculationTimeout, _clock);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            var dispatched = false;
            try
            {
                await using var session = await wami.OpenModelAsync(model, timeout.Token);
                dispatched = true;
                log("calculate", plan.CalculationRole, "dispatched", $"request {requestId}, {plan.Function}, attempt {attempt}");
                var result = await session.CalculateAsync(new WamiCalculationRequest(requestId, plan.Function, inputs), timeout.Token);
                return (result, CompositionStatus.Completed, null);
            }
            catch (WamiException error) when (error.Outcome == WamiCallOutcome.NotStarted && error.Code == WamiFailureCodes.Busy &&
                                              attempt < plan.MaximumBusyAttempts)
            {
                // Nothing ran, so a bounded repeat is safe; any other refusal is final.
                log("calculate", plan.CalculationRole, error.Code, $"Refused before start; retrying (attempt {attempt}).");
                if (plan.BusyRetryDelay is { } delay) await Task.Delay(delay, _clock, cancellationToken);
            }
            catch (WamiException error)
            {
                var status = error.Outcome == WamiCallOutcome.Ambiguous ? CompositionStatus.Ambiguous : CompositionStatus.Failed;
                log("calculate", plan.CalculationRole, error.Code, error.Message);
                return (null, status, error.Code);
            }
            catch (OperationCanceledException)
            {
                // Cancelling the call does not stop a WAMI worker that already started: once dispatched, the outcome is unknown.
                var byCaller = cancellationToken.IsCancellationRequested;
                var code = byCaller ? "CALCULATION_CANCELLED" : WamiFailureCodes.Timeout;
                if (!dispatched)
                {
                    log("calculate", plan.CalculationRole, code, "Stopped before the calculation was dispatched.");
                    return (null, byCaller ? CompositionStatus.Cancelled : CompositionStatus.Failed, byCaller ? null : code);
                }

                log("calculate", plan.CalculationRole, code, $"Request {requestId} may have run; result unknown. Not repeated.");
                return (null, CompositionStatus.Ambiguous, code);
            }
        }
    }
}
