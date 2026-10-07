using System.Net;
using System.Text;
using System.Text.Json;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Worker.DotNet.Execution;

if (Subjects.ExecutionRequestedV1 == Subjects.ExecutionRequestedV2)
{
    throw new InvalidOperationException("Execution V2 must coexist with V1 on a distinct subject.");
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
    private readonly ILogicModuleV1 _logicModule = new MultiInputModule();

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
            context.Inputs.Count != 2 ||
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

file sealed class EngineeringValueHandler(DateTimeOffset timestampUtc)
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
                unit = isFillage ? "%" : "spm",
                timestampUtc,
                quality = "Good",
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
