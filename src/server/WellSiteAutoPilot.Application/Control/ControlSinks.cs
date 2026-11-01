using System.Collections.Concurrent;
using WellSiteAutoPilot.Domain.Control;

namespace WellSiteAutoPilot.Application.Control;

public enum SimulatedReadbackBehavior
{
    // The target reports exactly what was commanded.
    Echo,

    // The target reports a different value than commanded.
    Mismatch,

    // The target cannot say.
    Unknown
}

public enum SimulatedDispatchFault
{
    None,

    // The command reached the target, then the call failed (timeout after applying).
    FailAfterApplying,

    // The call failed before the command reached the target.
    FailBeforeApplying
}

// Mock sink: records what would have been sent and never touches a provider. The two properties below exist so the
// ambiguous outcomes that a real sink will produce can be exercised; nothing in the product sets them.
public sealed class SimulatedControlCommandSink : IControlCommandSink
{
    private readonly ConcurrentDictionary<string, ControlDispatchRequest> sent = new();

    public string Kind => ControlSinkKinds.Simulator;

    public IReadOnlyCollection<ControlDispatchRequest> Sent => sent.Values.ToArray();

    public SimulatedReadbackBehavior Readback { get; set; } = SimulatedReadbackBehavior.Echo;

    public SimulatedDispatchFault Fault { get; set; } = SimulatedDispatchFault.None;

    public Task<ControlDispatchResult> DispatchAsync(
        ControlDispatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Fault == SimulatedDispatchFault.FailBeforeApplying)
            throw new TimeoutException("Simulated sink failure before the command was applied.");

        sent.TryAdd(request.IdempotencyKey, request);
        if (Fault == SimulatedDispatchFault.FailAfterApplying)
            throw new TimeoutException("Simulated sink failure after the command was applied.");

        return Task.FromResult(new ControlDispatchResult(true, $"sim:{request.ActionId:N}", "Simulated; no provider write."));
    }

    public Task<ControlReadback> ReadbackAsync(
        ControlDispatchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Readback == SimulatedReadbackBehavior.Unknown)
            return Task.FromResult(new ControlReadback(ControlReadbackState.Unknown));

        if (!sent.TryGetValue(request.IdempotencyKey, out var applied))
            return Task.FromResult(new ControlReadback(ControlReadbackState.NotApplied));

        var observed = Readback == SimulatedReadbackBehavior.Mismatch ? (applied.Value ?? 0m) + 1m : applied.Value;
        return Task.FromResult(new ControlReadback(ControlReadbackState.Applied, observed));
    }
}

// Real provider writes are disabled in this build; this sink exists so a refusal is explicit and auditable.
public sealed class RefusingProviderControlCommandSink : IControlCommandSink
{
    public string Kind => ControlSinkKinds.Provider;

    public Task<ControlDispatchResult> DispatchAsync(
        ControlDispatchRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ControlDispatchResult(false, "REAL_PROVIDER_WRITES_DISABLED",
            "Real provider writes are disabled."));

    public Task<ControlReadback> ReadbackAsync(
        ControlDispatchRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ControlReadback(ControlReadbackState.Unknown));
}
