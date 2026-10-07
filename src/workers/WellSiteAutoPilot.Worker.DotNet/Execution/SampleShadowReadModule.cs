using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

internal sealed class SampleShadowReadModule : ILogicModuleV1
{
    public LogicModuleIdentity Identity { get; } =
        new("weatherford.sample-shadow-read", "1.0.0");

    public ValueTask<LogicModuleExecutionResult> ExecuteAsync(
        LogicModuleExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var input = context.Inputs.Single();

        return ValueTask.FromResult(
            new LogicModuleExecutionResult(
                "SHADOW_MODULE_COMPLETED",
                [
                    new EngineeringResultValue(
                        "observed-value",
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
