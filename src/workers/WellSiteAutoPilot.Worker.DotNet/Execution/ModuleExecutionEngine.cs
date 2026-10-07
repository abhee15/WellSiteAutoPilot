using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

public sealed class ModuleExecutionEngine(
    GatewayDataClient gatewayDataClient,
    ILogicModuleResolver moduleResolver)
{
    public async Task<LogicModuleExecutionResult> ExecuteAsync(
        ExecutionRequestedV1 request,
        string correlationId,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var logicModule = moduleResolver.Resolve(
            request.ModuleId,
            request.ModuleVersion);

        var engineeringValue = await gatewayDataClient.GetCurrentAsync(
            request.AssetExternalId,
            request.Quantity,
            cancellationToken);

        var context = new LogicModuleExecutionContext(
            request.ExecutionId,
            logicModule.Identity,
            correlationId,
            evaluationTimeUtc,
            "{}",
            [
                new LogicAssetContext(
                    "asset",
                    request.AssetId,
                    "{}")
            ],
            [
                new LogicInputValue(
                    request.Quantity,
                    request.AssetId,
                    engineeringValue.Quantity,
                    engineeringValue.Value,
                    engineeringValue.Unit,
                    engineeringValue.TimestampUtc,
                    engineeringValue.Quality,
                    engineeringValue.Source)
            ]);

        return await logicModule.ExecuteAsync(
            context,
            cancellationToken);
    }
}
