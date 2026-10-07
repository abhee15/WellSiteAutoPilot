using WellSiteAutoPilot.ModuleSdk;

var assetId = Guid.NewGuid();
var executionId = Guid.NewGuid();
var module = new SampleObservationModule();

var context = new LogicModuleExecutionContext(
    executionId,
    module.Identity,
    "sdk-contract-test",
    DateTimeOffset.UtcNow,
    "{}",
    [new LogicAssetContext("well", assetId, "{}")],
    [
        new LogicInputValue(
            "pump-fillage",
            assetId,
            "PumpFillage",
            82m,
            "%",
            DateTimeOffset.UtcNow,
            "Good",
            "simulator:WELL-101")
    ]);

var result = await module.ExecuteAsync(context);

if (result.OutcomeCode != "SAMPLE_OBSERVATION_COMPLETED" ||
    result.EngineeringResults.Single().Value != 82m ||
    result.ControlIntents.Count != 0 ||
    result.Recommendations.Count != 0)
{
    throw new InvalidOperationException("Logic Module SDK execution contract failed.");
}

if (typeof(LogicModuleExecutionContext)
    .GetProperties()
    .Any(property =>
        property.Name.Contains("CygNet", StringComparison.OrdinalIgnoreCase) ||
        property.Name.Contains("WAMI", StringComparison.OrdinalIgnoreCase)))
{
    throw new InvalidOperationException("Provider-specific concepts leaked into the Logic Module SDK.");
}

Console.WriteLine("Logic Module SDK contract checks passed.");
return 0;

file sealed class SampleObservationModule : ILogicModuleV1
{
    public LogicModuleIdentity Identity { get; } =
        new("weatherford.sample-observation", "1.0.0");

    public ValueTask<LogicModuleExecutionResult> ExecuteAsync(
        LogicModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var input = context.Inputs.Single(item => item.RequirementId == "pump-fillage");

        return ValueTask.FromResult(
            new LogicModuleExecutionResult(
                "SAMPLE_OBSERVATION_COMPLETED",
                [
                    new EngineeringResultValue(
                        "observed-pump-fillage",
                        input.AssetId,
                        input.Quantity,
                        input.Value,
                        input.Unit)
                ],
                [],
                [],
                new Dictionary<string, string>()));
    }
}
