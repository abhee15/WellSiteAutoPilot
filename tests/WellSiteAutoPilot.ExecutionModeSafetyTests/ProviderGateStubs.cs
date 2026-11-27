using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Control;

/// <summary>A resolver the test steers, so every refusal and every catalog change can be produced on demand.</summary>
internal sealed class StubTargetResolver : IControlTargetResolver
{
    public Func<Guid, string, ControlTargetResolution> Behavior { get; set; } =
        (_, _) => ControlTargetResolution.Refused(GateCodes.TargetNoConnection);

    public Task<ControlTargetResolution> ResolveAsync(Guid assetId, string command,
        IReadOnlyCollection<ConfiguredLogicDataBinding> dataBindings, CancellationToken cancellationToken = default) =>
        Task.FromResult(Behavior(assetId, command));

    public static ControlTarget Target(string adapter = "simulator", decimal? min = 0, decimal? max = 12, bool enabled = true, decimal? step = null) =>
        new(Guid.NewGuid(), 1, adapter, "WELL-101", new Dictionary<string, string?>(),
            new ConnectionCommandCatalogEntry("SetSpeed", enabled, min, max, step, new Dictionary<string, string>(), 2, 30), 3);
}

/// <summary>A Provider sink that records what it was asked to send and reports it applied.</summary>
internal sealed class RecordingProviderSink : IControlCommandSink
{
    private readonly List<ControlDispatchRequest> requests = [];

    /// <summary>What the equipment holds before a command is sent; null means it cannot be read.</summary>
    public decimal? CurrentBefore { get; set; }

    public string Kind => ControlSinkKinds.Provider;

    public IReadOnlyList<ControlDispatchRequest> Requests => requests;

    public Task<ControlDispatchResult> DispatchAsync(ControlDispatchRequest request, CancellationToken cancellationToken = default)
    {
        requests.Add(request);
        return Task.FromResult(new ControlDispatchResult(true, $"prov:{request.ActionId:N}"));
    }

    public Task<ControlReadback> ReadbackAsync(ControlDispatchRequest request, CancellationToken cancellationToken = default)
    {
        var sent = requests.Any(item => item.IdempotencyKey == request.IdempotencyKey);
        if (sent) return Task.FromResult(new ControlReadback(ControlReadbackState.Applied, request.Value));
        return Task.FromResult(CurrentBefore is { } current
            ? new ControlReadback(ControlReadbackState.Applied, current)
            : new ControlReadback(ControlReadbackState.Unknown));
    }
}
