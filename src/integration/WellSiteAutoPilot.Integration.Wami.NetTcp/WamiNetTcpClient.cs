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

    // wellheadPressure maps to IModel.SetOperatingPointTopNode(-1, pressure) (-1 keeps the model's top node); WAMI's source
    // documents no unit for it, so it is taken in the model's own pressure unit. Setting it changes the session's model.
    public IReadOnlyList<WamiFunctionDescriptor> Functions { get; } =
    [
        new(NodalAnalysis, "Nodal-analysis operating point; optional wellhead (top node) pressure override.", WamiFunctionEffect.ChangesSessionModel,
            [WellheadPressure], ["operatingPoint.liquidRate", "operatingPoint.bottomholePressure"])
    ];

    public async ValueTask<WamiServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default)
    {
        using var channel = Open<ILicense>("ILicense");
        var status = await Guarded(channel, () => channel.Proxy.HasWAMILicense(), WamiCallOutcome.NotStarted, cancellationToken);
        if (status != WAMILicenseStatus.Valid)
            throw new WamiException(WamiFailureCodes.LicenseMissing, $"WAMI licence is {status}.", WamiCallOutcome.NotStarted);
        return new WamiServerInfo("unknown", ["WAMI"]);
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
        public async ValueTask<WamiCalculationResult> CalculateAsync(WamiCalculationRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Function != NodalAnalysis)
                throw new WamiException(WamiFailureCodes.FunctionUnsupported, $"No WAMI function '{request.Function}'.", WamiCallOutcome.NotStarted);
            if (request.Inputs.Keys.Any(k => k != WellheadPressure))
                throw new WamiException(WamiFailureCodes.InputInvalid, $"{NodalAnalysis} accepts only {WellheadPressure}.", WamiCallOutcome.NotStarted);

            // Dispatched from here on: failures and cancels are ambiguous.
            var result = await Guarded(channel, () =>
            {
                if (request.Inputs.TryGetValue(WellheadPressure, out var pressure))
                    channel.Proxy.SetOperatingPointTopNode(-1, (double)pressure.Value);
                return channel.Proxy.DoNodalAnalysis();
            }, WamiCallOutcome.Ambiguous, cancellationToken);
            if (result.Status != ResultStatus.Success || result.OperatingPoint is null)
            {
                var detail = result.ModelValidationResult is { ValidationErrors: > 0 } ? " The model failed validation." : "";
                throw new WamiException(WamiFailureCodes.CalculationFailed, $"WAMI returned {result.Status}.{detail}", WamiCallOutcome.Failed);
            }

            var point = result.OperatingPoint;
            return new WamiCalculationResult(
                request.Function,
                new Dictionary<string, WamiQuantity>
                {
                    ["operatingPoint.liquidRate"] = new(Convert.ToDecimal(point.LiquidRate, System.Globalization.CultureInfo.InvariantCulture), NativeUnits),
                    ["operatingPoint.bottomholePressure"] = new(Convert.ToDecimal(point.BottomholePressure, System.Globalization.CultureInfo.InvariantCulture), NativeUnits)
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
