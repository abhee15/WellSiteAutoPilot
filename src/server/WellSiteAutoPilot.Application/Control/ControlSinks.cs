using System.Collections.Concurrent;

namespace WellSiteAutoPilot.Application.Control;

public enum SimulatedReadbackBehavior
{
    Echo,
    Mismatch,
    Unknown
}

// Mock sink: records what would have been sent and never touches a provider.
public sealed class SimulatedControlCommandSink : IControlCommandSink
{
    private readonly ConcurrentDictionary<string, ControlDispatchRequest> sent = new();

    public string Kind => Domain.Control.ControlSinkKinds.Simulator;

    public IReadOnlyCollection<ControlDispatchRequest> Sent => sent.Values.ToArray();

    // Test hooks to exercise verification and ambiguous outcomes.
    public SimulatedReadbackBehavior Readback { get; set; } = SimulatedReadbackBehavior.Echo;

    public bool FailAfterApplying { get; set; }

    public Task<ControlDispatchResult> DispatchAsync(
        ControlDispatchRequest request, CancellationToken cancellationToken = default)
    {
        sent.TryAdd(request.IdempotencyKey, request);
        if (FailAfterApplying)
        {
            throw new TimeoutException("Simulated sink timeout after the command was applied.");
        }

        return Task.FromResult(new ControlDispatchResult(true, $"sim:{request.ActionId:N}", "Simulated; no provider write."));
    }

    public Task<ControlReadback> ReadbackAsync(
        ControlDispatchRequest request, CancellationToken cancellationToken = default)
    {
        if (Readback == SimulatedReadbackBehavior.Unknown)
        {
            return Task.FromResult(new ControlReadback(Domain.Control.ControlReadbackState.Unknown));
        }

        if (!sent.TryGetValue(request.IdempotencyKey, out var applied))
        {
            return Task.FromResult(new ControlReadback(Domain.Control.ControlReadbackState.NotApplied));
        }

        var observed = Readback == SimulatedReadbackBehavior.Mismatch ? (applied.Value ?? 0m) + 1m : applied.Value;
        return Task.FromResult(new ControlReadback(Domain.Control.ControlReadbackState.Applied, observed));
    }
}

// Real provider writes are disabled in this build; this sink exists so a refusal is explicit and auditable.
public sealed class RefusingProviderControlCommandSink : IControlCommandSink
{
    public string Kind => Domain.Control.ControlSinkKinds.Provider;

    public Task<ControlDispatchResult> DispatchAsync(
        ControlDispatchRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ControlDispatchResult(false, "REAL_PROVIDER_WRITES_DISABLED",
            "Real provider writes are disabled."));

    public Task<ControlReadback> ReadbackAsync(
        ControlDispatchRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ControlReadback(Domain.Control.ControlReadbackState.Unknown));
}
