using System.Text.Json;

namespace WellSiteAutoPilot.Integration.Wami;

public enum FakeWamiMode
{
    Normal = 0,
    /// <summary>Every worker is taken: refuses before starting.</summary>
    Busy = 1,
    Down = 2,
    NoLicense = 3,
    /// <summary>The calculation starts and never answers until cancelled.</summary>
    Hangs = 4
}

/// <summary>
/// Deterministic stand-in for a WAMI server, for offline tests. It is NOT WAMI: it solves a one-line inflow/tubing
/// balance from a small JSON "model" so that results change with the model bytes and the inputs, and it refuses the
/// way the real service is expected to (busy, down, licence, hang). It proves contract composition only.
/// Model: {"productivityIndex":2,"reservoirPressure":2500,"tubingGradient":0.35,"depth":6000}.
/// </summary>
public sealed class FakeWamiClient : IWamiClient
{
    public const string NodalAnalysis = "nodal-analysis";
    public const string Version = "fake-1";

    private int _opened;
    private int _dispatched;
    private int _completed;
    private int _live;

    public FakeWamiMode Mode { get; set; }
    public int SessionsOpened => Volatile.Read(ref _opened);
    public int CalculationsDispatched => Volatile.Read(ref _dispatched);
    public int CalculationsCompleted => Volatile.Read(ref _completed);
    public int SessionsOpen => Volatile.Read(ref _live);

    public IReadOnlyList<WamiFunctionDescriptor> Functions { get; } =
    [
        new(NodalAnalysis, "Operating point from the model's inflow and the wellhead pressure.", WamiFunctionEffect.None,
            ["wellheadPressure"], ["operatingPoint.liquidRate", "operatingPoint.bottomholePressure"])
    ];

    public ValueTask<WamiServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Refuse(checkLicense: true);
        return ValueTask.FromResult(new WamiServerInfo(Version, ["WAMI", "RRL"]));
    }

    public ValueTask<IWamiModelSession> OpenModelAsync(ModelArtifact model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        cancellationToken.ThrowIfCancellationRequested();
        Refuse(checkLicense: true);
        var parameters = Parse(model.Content);
        Interlocked.Increment(ref _opened);
        Interlocked.Increment(ref _live);
        return ValueTask.FromResult<IWamiModelSession>(new Session(this, parameters));
    }

    private void Refuse(bool checkLicense)
    {
        switch (Mode)
        {
            case FakeWamiMode.Down:
                throw new WamiException(WamiFailureCodes.Unavailable, "The WAMI server did not answer.", WamiCallOutcome.NotStarted);
            case FakeWamiMode.Busy:
                throw new WamiException(WamiFailureCodes.Busy, "All WAMI workers are busy.", WamiCallOutcome.NotStarted);
            case FakeWamiMode.NoLicense when checkLicense:
                throw new WamiException(WamiFailureCodes.LicenseMissing, "WAMI has no valid licence.", WamiCallOutcome.NotStarted);
        }
    }

    private static ModelParameters Parse(byte[] content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            return new ModelParameters(
                root.GetProperty("productivityIndex").GetDecimal(),
                root.GetProperty("reservoirPressure").GetDecimal(),
                root.GetProperty("tubingGradient").GetDecimal(),
                root.GetProperty("depth").GetDecimal());
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new WamiException(WamiFailureCodes.ModelRejected, "WAMI could not open the model file.", WamiCallOutcome.Failed, error);
        }
    }

    private sealed record ModelParameters(decimal ProductivityIndex, decimal ReservoirPressure, decimal TubingGradient, decimal Depth);

    private sealed class Session(FakeWamiClient owner, ModelParameters model) : IWamiModelSession
    {
        private bool _disposed;

        public async ValueTask<WamiCalculationResult> CalculateAsync(
            WamiCalculationRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Function != NodalAnalysis)
                throw new WamiException(WamiFailureCodes.FunctionUnsupported, $"WAMI has no function '{request.Function}'.", WamiCallOutcome.NotStarted);
            owner.Refuse(checkLicense: false);
            if (!request.Inputs.TryGetValue("wellheadPressure", out var whp) || !string.Equals(whp.Unit, "psia", StringComparison.Ordinal))
                throw new WamiException(WamiFailureCodes.InputInvalid, "wellheadPressure is required in psia.", WamiCallOutcome.NotStarted);

            Interlocked.Increment(ref owner._dispatched);
            if (owner.Mode == FakeWamiMode.Hangs) await Task.Delay(Timeout.Infinite, cancellationToken);

            var bottomhole = whp.Value + model.TubingGradient * model.Depth;
            var rate = Math.Max(0m, model.ProductivityIndex * (model.ReservoirPressure - bottomhole));
            Interlocked.Increment(ref owner._completed);
            return new WamiCalculationResult(
                request.Function,
                new Dictionary<string, WamiQuantity>
                {
                    ["operatingPoint.liquidRate"] = new(decimal.Round(rate, 3), "stb/d"),
                    ["operatingPoint.bottomholePressure"] = new(decimal.Round(bottomhole, 3), "psia")
                },
                Version,
                rate == 0m ? ["No inflow at this wellhead pressure."] : []);
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            Interlocked.Decrement(ref owner._live);
            return ValueTask.CompletedTask;
        }
    }
}
