using WellSiteAutoPilot.ModuleSdk;

namespace Weatherford.CygNetInputCheck;

/// <summary>Observe a supplied average-fillage input. This module has no CygNet client and issues no commands.</summary>
public sealed class EntryPoint : ILogicModuleV1
{
    public LogicModuleIdentity Identity { get; } = new("weatherford.cygnet-input-check.dotnet", "1.0.0");
    public ValueTask<LogicModuleExecutionResult> ExecuteAsync(LogicModuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var input = context.Inputs.SingleOrDefault();
        var valid = context.ModuleIdentity == Identity && context.Assets.Count == 1 && input is not null &&
            input.AssetId == context.Assets.Single().AssetId && input.RequirementId == "average-fillage" &&
            input.Quantity == "AveragePumpFillage" && input.Unit == "%" && input.Value is >= 0 and <= 100;
        var result = valid
            ? new LogicModuleExecutionResult("CYGNET_INPUT_CHECK_COMPLETED",
                [new EngineeringResultValue("observed-average-fillage", input!.AssetId, input.Quantity, input.Value, input.Unit)], [], [], new Dictionary<string, string>())
            : new LogicModuleExecutionResult("CYGNET_INPUT_INVALID", [], [], [], new Dictionary<string, string>()) { FailureCode = "CYGNET_INPUT_INVALID" };
        return ValueTask.FromResult(result);
    }
}
