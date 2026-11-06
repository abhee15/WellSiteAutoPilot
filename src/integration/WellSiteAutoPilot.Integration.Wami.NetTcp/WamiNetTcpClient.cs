// The legacy contract assembly has no nullable annotations, so generic results infer as non-null noise.
#nullable disable warnings
using System.ServiceModel;
using Weatherford.WAMI.Interfaces.Data;
using Weatherford.WAMI.Interfaces.Operations;

namespace WellSiteAutoPilot.Integration.Wami.NetTcp;

/// <summary>
/// WAMI over its own WCF net.tcp contract, as ForeSite calls it (WamiClientFactory): the router address plus
/// /IModel and /ILicense, Windows transport security, generous message limits and six-minute timeouts. WAMI keeps the
/// opened model on the server for the life of one channel, so one channel is one <see cref="IWamiModelSession"/>.
/// The Gateway's Windows account needs access to WAMI. NOT yet run against a real WAMI server.
/// </summary>
public sealed class WamiNetTcpClient : IWamiClient
{
    public const string NodalAnalysis = "nodal-analysis";
    public const string WellheadPressure = "wellheadPressure";
    public const string NativeUnits = "wami-native";

    private readonly Uri _router;
    private readonly NetTcpBinding _binding;

    public WamiNetTcpClient(Uri routerAddress, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(routerAddress);
        if (routerAddress.Scheme != "net.tcp") throw new ArgumentException("WAMI is addressed with net.tcp.", nameof(routerAddress));
        _router = new Uri(routerAddress.AbsoluteUri.TrimEnd('/') + "/");
        var limit = timeout ?? TimeSpan.FromMinutes(6);
        _binding = new NetTcpBinding
        {
            MaxReceivedMessageSize = int.MaxValue,
            SendTimeout = limit,
            ReceiveTimeout = limit,
            OpenTimeout = TimeSpan.FromSeconds(15)
        };
        _binding.ReaderQuotas.MaxArrayLength = int.MaxValue;
        _binding.ReaderQuotas.MaxStringContentLength = int.MaxValue;
        _binding.Security.Mode = SecurityMode.Transport;
        _binding.Security.Transport.ClientCredentialType = TcpClientCredentialType.Windows;
    }

    // wellheadPressure maps to IModel.SetOperatingPointTopNode(-1, pressure) (-1 keeps the model's top node). WAMI works in
    // oilfield units regardless of the model (SolutionPointData documents psia and STB/d); confirmed live: raising the
    // pressure lowers the rate. Setting it changes the session's model.
    public const string IprFlowRate = "ipr-flow-rate";
    public const string PvtProperties = "pvt-properties";
    public const string PerformanceCurve = "well-performance-curve";

    public IReadOnlyList<WamiFunctionDescriptor> Functions => Descriptors;

    private static readonly IReadOnlyList<WamiFunctionDescriptor> Descriptors =
    [
        new(NodalAnalysis, "Nodal-analysis operating point; optional wellhead (top node) pressure override.", WamiFunctionEffect.ChangesSessionModel,
            [WellheadPressure], ["operatingPoint.liquidRate", "operatingPoint.bottomholePressure"]),
        new(IprFlowRate, "Inflow rate of reservoir layer 1 at a flowing bottomhole pressure.", WamiFunctionEffect.None,
            ["bottomholePressure"], ["ipr.liquidRate"]),
        new(PvtProperties, "Fluid properties at a pressure, temperature and test rates.", WamiFunctionEffect.None,
            ["pressure", "temperature", "oilRate", "waterRate", "gasRate"],
            ["pvt.bubblePointPressure", "pvt.solutionGor", "pvt.oilFormationVolumeFactor", "pvt.zFactor", "pvt.oilViscosity"]),
        new(PerformanceCurve, "Well performance (outflow vs rate) curve for a natural-flow well; pointCount points, outputs indexed curve[i].*.", WamiFunctionEffect.None,
            ["pointCount"], ["curve[i].liquidRate", "curve[i].wellheadPressure"])
    ];

    public async ValueTask<WamiServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        using var channel = Open<ILicense>("ILicense");
        var status = await Guarded(channel, () => channel.Proxy.HasWAMILicense(), WamiCallOutcome.NotStarted, cancellationToken);
        if (status != WAMILicenseStatus.Valid)
            throw new WamiException(WamiFailureCodes.LicenseMissing, $"WAMI licence is {status}.", WamiCallOutcome.NotStarted);
        // Per-feature licences tell a module which analyses this server can run; read-only calls.
        var checks = new (string Feature, Func<WAMILicenseStatus> Check)[]
        {
            ("RRL", channel.Proxy.HasRRLLicense), ("ESP", channel.Proxy.HasESPLicense), ("GasLift", channel.Proxy.HasGasLiftLicense),
            ("PlungerLift", channel.Proxy.HasPlungerLiftLicense), ("PCP", channel.Proxy.HasPCPLicense), ("FlowAssurance", channel.Proxy.HasFlowAssuranceLicense),
            ("RRLGibbs", channel.Proxy.HasRRLGibbsLicense), ("RRLEJ", channel.Proxy.HasRRLEJLicense),
            ("RRLDesignVertical", channel.Proxy.HasRRLDesignVerticalLicense), ("RRLPatternMatching", channel.Proxy.HasRRLPatternMatchingLicense)
        };
        var features = new List<string> { "WAMI" };
        foreach (var (feature, check) in checks)
            if (await Guarded(channel, check, WamiCallOutcome.NotStarted, cancellationToken) == WAMILicenseStatus.Valid) features.Add(feature);
        return new WamiServerInfo("unknown", features);
    }

    public async ValueTask<IWamiModelSession> OpenModelAsync(ModelArtifact model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var channel = Open<IModel>("IModel");
        try
        {
            var license = await Guarded(channel, () => channel.Proxy.HasWAMILicense(), WamiCallOutcome.NotStarted, cancellationToken);
            if (license != WAMILicenseStatus.Valid)
                throw new WamiException(WamiFailureCodes.LicenseMissing, $"WAMI licence is {license}.", WamiCallOutcome.NotStarted);
            var file = await Guarded(channel, () => channel.Proxy.OpenFileData(new FileData { Name = model.Ref.ModelId, Extension = "", File = model.Content }),
                WamiCallOutcome.NotStarted, cancellationToken);
            if (file != FileStatus.Success)
                throw new WamiException(WamiFailureCodes.ModelRejected, $"WAMI could not open the model file ({file}).", WamiCallOutcome.Failed);
            return new Session(channel);
        }
        catch
        {
            channel.Dispose();
            throw;
        }
    }

    private Channel<T> Open<T>(string endpoint)
    {
        var factory = new ChannelFactory<T>(_binding, new EndpointAddress(new Uri(_router, endpoint)));
        var proxy = factory.CreateChannel();
        try
        {
            ((IClientChannel)proxy).Open();
        }
        catch (Exception error) when (error is CommunicationException or TimeoutException or InvalidOperationException)
        {
            ((IClientChannel)proxy).Abort();
            factory.Abort();
            throw new WamiException(WamiFailureCodes.Unavailable, "WAMI could not be reached.", WamiCallOutcome.NotStarted, error);
        }

        return new Channel<T>(factory, proxy);
    }

    /// <summary>
    /// WCF calls here are synchronous. Cancelling aborts the channel; a worker already running keeps going, so when
    /// <paramref name="failureOutcome"/> is Ambiguous a timeout or lost connection is reported as ambiguous.
    /// </summary>
    internal static async Task<TResult> Guarded<T, TResult>(Channel<T> channel, Func<TResult> call, WamiCallOutcome failureOutcome, CancellationToken cancellationToken)
    {
        using var abort = cancellationToken.Register(channel.Abort);
        try
        {
            return await Task.Run(call, CancellationToken.None);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (TimeoutException error)
        {
            throw new WamiException(WamiFailureCodes.Timeout, "WAMI did not answer in time.", failureOutcome == WamiCallOutcome.NotStarted ? WamiCallOutcome.NotStarted : WamiCallOutcome.Ambiguous, error);
        }
        catch (ServerTooBusyException error)
        {
            throw new WamiException(WamiFailureCodes.Busy, "WAMI is too busy.", WamiCallOutcome.NotStarted, error);
        }
        catch (FaultException<WorkerLimitFault> error)
        {
            // The router faults before routing when MaxAllowedWorkers workers are in use: it never queues and nothing ran.
            throw new WamiException(WamiFailureCodes.Busy, $"WAMI has no free worker (limit {error.Detail?.MaxAllowedWorkers}).", WamiCallOutcome.NotStarted, error);
        }
        catch (FaultException error) when (error.Message.Contains("workers are already being used", StringComparison.OrdinalIgnoreCase))
        {
            // Observed live: the 11th concurrent session arrives as an untyped fault with this text (limit = MaxAllowedWorkers).
            throw new WamiException(WamiFailureCodes.Busy, "WAMI has no free worker.", WamiCallOutcome.NotStarted, error);
        }
        catch (FaultException error)
        {
            throw new WamiException(WamiFailureCodes.CalculationFailed, "WAMI reported a fault.", WamiCallOutcome.Failed, error);
        }
        catch (CommunicationException error)
        {
            throw new WamiException(WamiFailureCodes.Unavailable, "The WAMI connection failed.", failureOutcome, error);
        }
    }

    internal sealed class Channel<T>(ChannelFactory<T> factory, T proxy) : IDisposable
    {
        public T Proxy { get; } = proxy;

        public void Abort()
        {
            ((IClientChannel)Proxy).Abort();
        }

        public void Dispose()
        {
            var channel = (IClientChannel)Proxy;
            try
            {
                if (channel.State == CommunicationState.Opened) channel.Close(TimeSpan.FromSeconds(5));
                else channel.Abort();
            }
            catch (Exception error) when (error is CommunicationException or TimeoutException)
            {
                channel.Abort();
            }

            factory.Abort();
        }
    }

    private sealed class Session(Channel<IModel> channel) : IWamiModelSession
    {
        // Read-only functions: they do not change the session model, so a repeat after an ambiguous failure is safe.
        private async ValueTask<WamiCalculationResult> CalculateOtherAsync(WamiCalculationRequest request, CancellationToken cancellationToken)
        {
            static decimal Number(WamiCalculationRequest r, string name, string unit)
            {
                if (!r.Inputs.TryGetValue(name, out var q))
                    throw new WamiException(WamiFailureCodes.InputInvalid, $"{r.Function} needs {name}.", WamiCallOutcome.NotStarted);
                if (q.Unit != unit)
                    throw new WamiException(WamiFailureCodes.InputInvalid, $"{name} must be in {unit}.", WamiCallOutcome.NotStarted);
                return q.Value;
            }
            static WamiQuantity Q(double value, string unit) => new(Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture), unit);

            var descriptor = Descriptors.FirstOrDefault(d => d.Name == request.Function)
                ?? throw new WamiException(WamiFailureCodes.FunctionUnsupported, $"No WAMI function '{request.Function}'.", WamiCallOutcome.NotStarted);
            if (request.Inputs.Keys.Any(k => !descriptor.Inputs.Contains(k)) || descriptor.Inputs.Any(k => !request.Inputs.ContainsKey(k)))
                throw new WamiException(WamiFailureCodes.InputInvalid, $"{request.Function} takes exactly: {string.Join(", ", descriptor.Inputs)}.", WamiCallOutcome.NotStarted);

            var outputs = new Dictionary<string, WamiQuantity>();
            switch (request.Function)
            {
                case IprFlowRate:
                    var pressure = (double)Number(request, "bottomholePressure", "psia");
                    var rate = await Guarded(channel, () => channel.Proxy.GetIPRFlowRate(0, pressure), WamiCallOutcome.Ambiguous, cancellationToken);
                    outputs["ipr.liquidRate"] = Q(rate, "stb/d");
                    break;
                case PvtProperties:
                    var args = new[] { ("pressure", "psia"), ("temperature", "degF"), ("oilRate", "stb/d"), ("waterRate", "stb/d"), ("gasRate", "MMscf/d") }
                        .Select(a => (double)Number(request, a.Item1, a.Item2)).ToArray();
                    var pvt = await Guarded(channel, () => channel.Proxy.CalculatePVTData(args[0], args[1], args[2], args[3], args[4]), WamiCallOutcome.Ambiguous, cancellationToken);
                    if (pvt is null) throw new WamiException(WamiFailureCodes.CalculationFailed, "WAMI returned no PVT data.", WamiCallOutcome.Failed);
                    outputs["pvt.bubblePointPressure"] = Q(pvt.BubblePointPressure, "psia");
                    outputs["pvt.solutionGor"] = Q(pvt.SolutionGOR, "scf/stb");
                    outputs["pvt.oilFormationVolumeFactor"] = Q(pvt.OilFormationVolumeFactor, "rb/stb");
                    outputs["pvt.zFactor"] = Q(pvt.ZFactor, "");
                    outputs["pvt.oilViscosity"] = Q(pvt.OilViscosity, "cp");
                    break;
                case PerformanceCurve:
                    var count = request.Inputs["pointCount"];
                    if (count.Unit != "" || count.Value < 2 || count.Value > 100 || count.Value != decimal.Truncate(count.Value))
                        throw new WamiException(WamiFailureCodes.InputInvalid, "pointCount must be a whole number from 2 to 100.", WamiCallOutcome.NotStarted);
                    var curve = await Guarded(channel, () => channel.Proxy.GenerateWellPerformanceCurve((int)count.Value), WamiCallOutcome.Ambiguous, cancellationToken);
                    var points = curve?.wellPerformanceCurvePoints;
                    if (points is null || points.Length == 0) throw new WamiException(WamiFailureCodes.CalculationFailed, "WAMI returned no performance curve.", WamiCallOutcome.Failed);
                    for (var i = 0; i < points.Length; i++)
                    {
                        outputs[$"curve[{i}].liquidRate"] = Q(points[i].LiquidRate, "stb/d");
                        outputs[$"curve[{i}].wellheadPressure"] = Q(points[i].WellHeadPressure, "psia");
                    }
                    break;
            }

            return new WamiCalculationResult(request.Function, outputs, "wami", []);
        }

        public async ValueTask<WamiCalculationResult> CalculateAsync(WamiCalculationRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Function != NodalAnalysis) return await CalculateOtherAsync(request, cancellationToken);
            if (request.Inputs.Keys.Any(k => k != WellheadPressure))
                throw new WamiException(WamiFailureCodes.InputInvalid, $"{NodalAnalysis} accepts only {WellheadPressure}.", WamiCallOutcome.NotStarted);

            if (request.Inputs.TryGetValue(WellheadPressure, out var supplied) && supplied.Unit != "psia")
                throw new WamiException(WamiFailureCodes.InputInvalid, $"{WellheadPressure} must be in psia.", WamiCallOutcome.NotStarted);

            // Dispatched from here on: failures and cancels are ambiguous.
            var result = await Guarded(channel, () =>
            {
                if (request.Inputs.TryGetValue(WellheadPressure, out var pressure))
                    channel.Proxy.SetOperatingPointTopNode(-1, (double)pressure.Value);
                return channel.Proxy.DoNodalAnalysis();
            }, WamiCallOutcome.Ambiguous, cancellationToken);
            // The installed 7.2 contract has no Status/validation members (the ForeSite 7.7 build does); a missing operating point is the failure signal.
            if (result?.OperatingPoint is null)
                throw new WamiException(WamiFailureCodes.CalculationFailed, "WAMI returned no operating point.", WamiCallOutcome.Failed);
            var point = result.OperatingPoint;
            return new WamiCalculationResult(
                request.Function,
                new Dictionary<string, WamiQuantity>
                {
                    ["operatingPoint.liquidRate"] = new(Convert.ToDecimal(point.LiquidRate, System.Globalization.CultureInfo.InvariantCulture), "stb/d"),
                    ["operatingPoint.bottomholePressure"] = new(Convert.ToDecimal(point.BottomholePressure, System.Globalization.CultureInfo.InvariantCulture), "psia")
                },
                "wami",
                point.OperatingPointStatus is { Length: > 0 } status ? [status] : []);
        }

        public ValueTask DisposeAsync()
        {
            channel.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
