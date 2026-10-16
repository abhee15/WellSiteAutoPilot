using WellSiteAutoPilot.Failures;
using System.Net;
using System.Text;
using System.Text.Json;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Worker.DotNet.Execution;

if (Subjects.ExecutionRequestedV1 == Subjects.ExecutionRequestedV2)
{
    throw new InvalidOperationException("Execution V2 must coexist with V1 on a distinct subject.");
}

// JetStream timing is validated before consumers start (for V1 and V2).
var leaseSettings = new JetStreamConsumerSettings().Validate();
if (leaseSettings.RequestAckWait != TimeSpan.FromSeconds(120) ||
    leaseSettings.ResultAckWait != TimeSpan.FromSeconds(30) ||
    leaseSettings.RequestProgressInterval != TimeSpan.FromSeconds(20) ||
    leaseSettings.MaxDeliver != 5)
{
    throw new InvalidOperationException("Default JetStream delivery policy drifted.");
}

foreach (var invalid in new[]
{
    new JetStreamConsumerSettings(RequestAckWaitSeconds: 9),
    new JetStreamConsumerSettings(RequestAckWaitSeconds: 901),
    new JetStreamConsumerSettings(ResultAckWaitSeconds: 0),
    new JetStreamConsumerSettings(RequestAckWaitSeconds: 30, RequestProgressSeconds: 11),
    new JetStreamConsumerSettings(RequestProgressSeconds: 0),
    new JetStreamConsumerSettings(MaxDeliver: 0),
    new JetStreamConsumerSettings(MaxDeliver: 21)
})
{
    try
    {
        invalid.Validate();
        throw new InvalidOperationException("Invalid JetStream configuration was accepted.");
    }
    catch (ArgumentOutOfRangeException)
    {
    }
}

// Simulate slow provider/module execution: progress ACKs continue while work
// is running, but stop once processing is finished.
var heartbeatCount = 0;
var processingObserved = false;
await JetStreamProgressLease.RunAsync(
    async cancellation =>
    {
        await Task.Delay(TimeSpan.FromMilliseconds(180), cancellation);
        processingObserved = true;
    },
    cancellation =>
    {
        cancellation.ThrowIfCancellationRequested();
        Interlocked.Increment(ref heartbeatCount);
        return ValueTask.CompletedTask;
    },
    TimeSpan.FromMilliseconds(20));

if (!processingObserved || Volatile.Read(ref heartbeatCount) < 3)
{
    throw new InvalidOperationException("Long-running module did not renew its delivery lease.");
}
var completedHeartbeatCount = Volatile.Read(ref heartbeatCount);
await Task.Delay(TimeSpan.FromMilliseconds(70));
if (Volatile.Read(ref heartbeatCount) != completedHeartbeatCount)
{
    throw new InvalidOperationException("Progress ACK continued after request completion.");
}

// A failed heartbeat must cancel the in-flight operation and prevent a
// successful request ACK. The caller handles redelivery and investigation.
var leaseFailureCanceledWork = false;
try
{
    await JetStreamProgressLease.RunAsync(
        async cancellation =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                leaseFailureCanceledWork = true;
                throw;
            }
        },
        _ => throw new IOException("simulated JetStream progress connection failure"),
        TimeSpan.FromMilliseconds(20));
    throw new InvalidOperationException("Lost JetStream lease was treated as successful.");
}
catch (IOException exception) when (exception.Message.Contains("simulated JetStream", StringComparison.Ordinal))
{
}

if (!leaseFailureCanceledWork)
{
    throw new InvalidOperationException("Heartbeat failure did not cancel executing module work.");
}

var assetId = Guid.NewGuid();
var executionId = Guid.NewGuid();
var now = DateTimeOffset.UtcNow;

var request = new ExecutionRequestedV2(
    executionId,
    Guid.NewGuid(),
    Guid.NewGuid(),
    "weatherford.multi-input-test",
    "1.0.0",
    "Shadow",
    """{"target":75}""",
    [
        new ExecutionAssetBindingV2(
            "well",
            assetId,
            """{"target":80}""")
    ],
    [
        new ExecutionInputBindingV2(
            "pump-fillage",
            assetId,
            "simulator",
            "WELL-101",
            "PumpFillage",
            "Current",
            "%",
            60,
            false,
            "{}"),
        new ExecutionInputBindingV2(
            "pumping-speed",
            assetId,
            "simulator",
            "WELL-101",
            "PumpingSpeed",
            "Current",
            "spm",
            60,
            false,
            "{}")
    ],
    now);

var httpClient = new HttpClient(new EngineeringValueHandler(now))
{
    BaseAddress = new Uri("http://127.0.0.1:5081")
};

var engine = new ModuleExecutionEngine(
    new GatewayDataClient(httpClient),
    new TestModuleResolver());

var result = await engine.ExecuteAsync(
    request,
    "execution-v2-test",
    now);

if (result.OutcomeCode != "MULTI_INPUT_COMPLETED" ||
    result.EngineeringResults.Count != 2 ||
    result.EngineeringResults.Sum(item => item.Value) != 94m)
{
    throw new InvalidOperationException(
        "Execution V2 did not construct the expected multi-input SDK context.");
}

var observed = await engine.ExecuteObservedAsync(request, "observed-input-test", now);
if (observed.InputObservations.Count != 2 || observed.InputObservations.Any(input =>
    input.Quality != "Good" || input.TimestampUtc != now || string.IsNullOrWhiteSpace(input.Source)))
    throw new InvalidOperationException("Execution input provenance was not preserved.");

foreach (var quality in new[] { "Bad", "Unknown", "Unexpected", "Uncertain" })
{
    using var badClient = new HttpClient(new EngineeringValueHandler(now, quality: quality)) { BaseAddress = new Uri("http://127.0.0.1:5081") };
    var badEngine = new ModuleExecutionEngine(new GatewayDataClient(badClient), new TestModuleResolver());
    try
    {
        await badEngine.ExecuteAsync(request, "bad-quality-test", now);
        throw new InvalidOperationException("Unsupported input quality was accepted.");
    }
    catch (WellSiteAutoPilotException exception) when (exception.Code == "EXECUTION_INPUT_QUALITY_REJECTED") { }
}

// Reject unsafe future-dated telemetry before invoking logic.
using (var futureClient = new HttpClient(new EngineeringValueHandler(now.AddMinutes(5)))
{
    BaseAddress = new Uri("http://127.0.0.1:5081")
})
{
    var futureEngine = new ModuleExecutionEngine(new GatewayDataClient(futureClient), new TestModuleResolver());
    try
    {
        await futureEngine.ExecuteAsync(request, "future-data-test", now);
        throw new InvalidOperationException("Future-dated telemetry was accepted.");
    }
    catch (WellSiteAutoPilotException exception) when (exception.Code == "EXECUTION_INPUT_FUTURE")
    {
    }
}

// Reject provider measurements that violate the requested unit.
using (var wrongUnitClient = new HttpClient(new EngineeringValueHandler(now, wrongUnit: true))
{
    BaseAddress = new Uri("http://127.0.0.1:5081")
})
{
    var wrongUnitEngine = new ModuleExecutionEngine(new GatewayDataClient(wrongUnitClient), new TestModuleResolver());
    try
    {
        await wrongUnitEngine.ExecuteAsync(request, "wrong-unit-test", now);
        throw new InvalidOperationException("Mismatched engineering unit was accepted.");
    }
    catch (WellSiteAutoPilotException exception) when (exception.Code == "EXECUTION_INPUT_QUANTITY_MISMATCH")
    {
    }
}

// A configured module may request many inputs. Reads must remain ordered,
// but the worker must not overload the gateway with unbounded concurrency.
var largeInputs = Enumerable.Range(0, 24)
    .Select(index => new ExecutionInputBindingV2(
        $"input-{index}", assetId, "simulator", "WELL-101",
        "PumpFillage", "Current", "%", 60, false, "{}"))
    .ToArray();
var largeRequest = request with { Inputs = largeInputs };
var boundedHandler = new ConcurrentReadHandler(now);
using (var boundedClient = new HttpClient(boundedHandler)
{
    BaseAddress = new Uri("http://127.0.0.1:5081")
})
{
    var boundedEngine = new ModuleExecutionEngine(
        new GatewayDataClient(boundedClient),
        new TestModuleResolver());
    var boundedResult = await boundedEngine.ExecuteAsync(
        largeRequest, "bounded-input-test", now);
    if (boundedHandler.MaximumInFlight > 8 ||
        boundedHandler.MaximumInFlight < 2 ||
        boundedHandler.TotalRequests != largeInputs.Length ||
        boundedResult.EngineeringResults.Count != largeInputs.Length ||
        !boundedResult.EngineeringResults.Select(item => item.Id)
            .SequenceEqual(largeInputs.Select(item => item.RequirementId)))
    {
        throw new InvalidOperationException(
            "Multi-input execution exceeded the gateway concurrency budget or lost input ordering.");
    }
}

// A lower configured budget must be enforced without changing result order.
var configuredHandler = new ConcurrentReadHandler(now);
using (var configuredClient = new HttpClient(configuredHandler)
{
    BaseAddress = new Uri("http://127.0.0.1:5081")
})
{
    var configuredEngine = new ModuleExecutionEngine(
        new GatewayDataClient(configuredClient), new TestModuleResolver(), 2);
    var configuredResult = await configuredEngine.ExecuteAsync(
        largeRequest, "configured-concurrency-test", now);
    if (configuredHandler.MaximumInFlight > 2 ||
        configuredHandler.TotalRequests != largeInputs.Length ||
        !configuredResult.EngineeringResults.Select(item => item.Id)
            .SequenceEqual(largeInputs.Select(item => item.RequirementId)))
    {
        throw new InvalidOperationException("Configured concurrency limit was not honored.");
    }
}

// Invalid limits fail at construction rather than disabling the safety budget.
try
{
    _ = new ModuleExecutionEngine(new GatewayDataClient(httpClient), new TestModuleResolver(), 0);
    throw new InvalidOperationException("Invalid concurrency budget was accepted.");
}
catch (ArgumentOutOfRangeException exception) when (exception.ParamName == "maximumConcurrentInputReads")
{
}

// Cancellation must prevent dispatching inputs and invoking module logic.
using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel();
    var canceledHandler = new ConcurrentReadHandler(now);
    using var canceledClient = new HttpClient(canceledHandler)
    {
        BaseAddress = new Uri("http://127.0.0.1:5081")
    };
    var canceledEngine = new ModuleExecutionEngine(
        new GatewayDataClient(canceledClient), new TestModuleResolver(), 2);
    try
    {
        await canceledEngine.ExecuteAsync(
            largeRequest, "cancellation-test", now, canceled.Token);
        throw new InvalidOperationException("Canceled execution unexpectedly succeeded.");
    }
    catch (OperationCanceledException) when (canceled.IsCancellationRequested)
    {
    }
    if (canceledHandler.TotalRequests != 0)
    {
        throw new InvalidOperationException("Canceled execution dispatched provider requests.");
    }
}

// Provider transport failures must fail the entire execution, not return partial inputs.
using (var failingClient = new HttpClient(new FailingReadHandler())
{
    BaseAddress = new Uri("http://127.0.0.1:5081")
})
{
    var failingEngine = new ModuleExecutionEngine(
        new GatewayDataClient(failingClient), new TestModuleResolver(), 2);
    try
    {
        await failingEngine.ExecuteAsync(request, "provider-failure-test", now);
        throw new InvalidOperationException("Failed provider request was accepted.");
    }
    catch (HttpRequestException)
    {
    }
}

var serialized = JsonSerializer.Serialize(
    request,
    new JsonSerializerOptions(JsonSerializerDefaults.Web));

var roundTrip = JsonSerializer.Deserialize<ExecutionRequestedV2>(
    serialized,
    new JsonSerializerOptions(JsonSerializerDefaults.Web));

if (roundTrip is null ||
    roundTrip.Inputs.Count != 2 ||
    roundTrip.Assets.Single().Role != "well" ||
    roundTrip.ConfiguredLogicId != request.ConfiguredLogicId)
{
    throw new InvalidOperationException(
        "Execution V2 message did not round-trip.");
}

Console.WriteLine("Execution V2 multi-input contract and worker context checks passed.");
return 0;

file sealed class TestModuleResolver : ILogicModuleResolver
{
    private readonly MultiInputModule _logicModule = new();

    public ILogicModuleV1 Resolve(string moduleId, string version)
    {
        if (moduleId != _logicModule.Identity.ModuleId ||
            version != _logicModule.Identity.Version)
        {
            throw new InvalidOperationException("Unexpected module identity.");
        }

        return _logicModule;
    }
}

file sealed class MultiInputModule : ILogicModuleV1
{
    public LogicModuleIdentity Identity { get; } =
        new("weatherford.multi-input-test", "1.0.0");

    public ValueTask<LogicModuleExecutionResult> ExecuteAsync(
        LogicModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (context.Assets.Single().Role != "well" ||
            context.Inputs.Count == 0 ||
            context.ParametersJson != """{"target":75}""")
        {
            throw new InvalidOperationException(
                "Worker did not construct the expected module execution context.");
        }

        return ValueTask.FromResult(
            new LogicModuleExecutionResult(
                "MULTI_INPUT_COMPLETED",
                context.Inputs
                    .Select(input => new EngineeringResultValue(
                        input.RequirementId,
                        input.AssetId,
                        input.Quantity,
                        input.Value,
                        input.Unit))
                    .ToArray(),
                [],
                [],
                new Dictionary<string, string>()));
    }
}

file sealed class EngineeringValueHandler(DateTimeOffset timestampUtc, bool wrongUnit = false, string quality = "Good")
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var query = request.RequestUri?.Query ?? string.Empty;
        var isFillage = query.Contains(
            "PumpFillage",
            StringComparison.OrdinalIgnoreCase);

        var payload = JsonSerializer.Serialize(
            new
            {
                quantity = isFillage ? "PumpFillage" : "PumpingSpeed",
                value = isFillage ? 82m : 12m,
                unit = wrongUnit ? "invalid-unit" : isFillage ? "%" : "spm",
                timestampUtc,
                quality,
                source = "simulator:WELL-101"
            });

        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    payload,
                    Encoding.UTF8,
                    "application/json")
            });
    }
}

file sealed class ConcurrentReadHandler(DateTimeOffset timestampUtc) : HttpMessageHandler
{
    private int _inFlight;
    private int _maximumInFlight;
    private int _totalRequests;

    public int MaximumInFlight => Volatile.Read(ref _maximumInFlight);
    public int TotalRequests => Volatile.Read(ref _totalRequests);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var active = Interlocked.Increment(ref _inFlight);
        Interlocked.Increment(ref _totalRequests);
        try
        {
            var observed = Volatile.Read(ref _maximumInFlight);
            while (active > observed)
            {
                var previous = Interlocked.CompareExchange(
                    ref _maximumInFlight, active, observed);
                if (previous == observed)
                {
                    break;
                }
                observed = previous;
            }

            await Task.Delay(25, cancellationToken);
            var payload = JsonSerializer.Serialize(new
            {
                quantity = "PumpFillage",
                value = 82m,
                unit = "%",
                timestampUtc,
                quality = "Good",
                source = "simulator:WELL-101"
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}

file sealed class FailingReadHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        throw new HttpRequestException("Simulated provider transport failure.");
}
