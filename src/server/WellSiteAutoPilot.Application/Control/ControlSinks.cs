using System.Collections.Concurrent;

namespace WellSiteAutoPilot.Application.Control;

// Mock sink: records what would have been sent and never touches a provider.
public sealed class SimulatedControlCommandSink : IControlCommandSink
{
    private readonly ConcurrentDictionary<string, ControlDispatchRequest> sent = new();

    public string Kind => Domain.Control.ControlSinkKinds.Simulator;

    public IReadOnlyCollection<ControlDispatchRequest> Sent => sent.Values.ToArray();

    public Task<ControlDispatchResult> DispatchAsync(
        ControlDispatchRequest request, CancellationToken cancellationToken = default)
    {
        sent.TryAdd(request.IdempotencyKey, request);
        return Task.FromResult(new ControlDispatchResult(true, $"sim:{request.ActionId:N}", "Simulated; no provider write."));
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
}
