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

    public async Task<LogicModuleExecutionResult> ExecuteAsync(
        ExecutionRequestedV2 request,
        string correlationId,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        if (request.Assets.Count == 0)
        {
            throw new InvalidOperationException(
                "Execution request must contain at least one Asset binding.");
        }

        var logicModule = moduleResolver.Resolve(
            request.ModuleId,
            request.ModuleVersion);

        var inputTasks = request.Inputs
            .Select(binding => ReadInputAsync(
                binding,
                evaluationTimeUtc,
                cancellationToken))
            .ToArray();

        var inputs = await Task.WhenAll(inputTasks);

        var context = new LogicModuleExecutionContext(
            request.ExecutionId,
            logicModule.Identity,
            correlationId,
            evaluationTimeUtc,
            request.ParametersJson,
            request.Assets
                .Select(asset => new LogicAssetContext(
                    asset.Role,
                    asset.AssetId,
                    asset.ParameterOverridesJson))
                .ToArray(),
            inputs);

        return await logicModule.ExecuteAsync(
            context,
            cancellationToken);
    }

    private async Task<LogicInputValue> ReadInputAsync(
        ExecutionInputBindingV2 binding,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(binding.Access, "Current", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Execution input '{binding.RequirementId}' uses unsupported access '{binding.Access}'.");
        }

        var value = await gatewayDataClient.GetCurrentAsync(
            binding.ProviderAssetExternalId,
            binding.Quantity,
            cancellationToken);

        if (string.Equals(value.Quality, "Bad", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value.Quality, "Unknown", StringComparison.OrdinalIgnoreCase) ||
            (!binding.AllowUncertainQuality &&
             string.Equals(value.Quality, "Uncertain", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Execution input '{binding.RequirementId}' does not satisfy the required data quality.");
        }

        if (binding.MaximumAgeSeconds is > 0 &&
            evaluationTimeUtc - value.TimestampUtc >
            TimeSpan.FromSeconds(binding.MaximumAgeSeconds.Value))
        {
            throw new InvalidOperationException(
                $"Execution input '{binding.RequirementId}' is stale.");
        }

        return new LogicInputValue(
            binding.RequirementId,
            binding.AssetId,
            value.Quantity,
            value.Value,
            value.Unit,
            value.TimestampUtc,
            value.Quality,
            value.Source);
    }
}
