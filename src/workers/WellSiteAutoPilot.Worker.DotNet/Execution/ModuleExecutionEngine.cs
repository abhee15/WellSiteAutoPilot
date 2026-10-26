using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

public sealed class ModuleExecutionEngine(
    GatewayDataClient gatewayDataClient,
    ILogicModuleResolver moduleResolver,
    int maximumConcurrentInputReads = 8,
    DotNetModulePackageHost? packageHost = null)
{
    private readonly int _maximumConcurrentInputReads = maximumConcurrentInputReads is >= 1 and <= 64
        ? maximumConcurrentInputReads
        : throw new ArgumentOutOfRangeException(nameof(maximumConcurrentInputReads),
            "Concurrent input reads must be between 1 and 64.");

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

        var result = await logicModule.ExecuteAsync(
            context,
            cancellationToken);
        EnsureModuleSucceeded(result);
        return result;
    }

    public async Task<LogicModuleExecutionResult> ExecuteAsync(
        ExecutionRequestedV2 request, string correlationId, DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken = default) =>
        (await ExecuteObservedAsync(request, correlationId, evaluationTimeUtc, cancellationToken)).Result;

    public async Task<ObservedExecutionResult> ExecuteObservedAsync(
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

        return await ExecuteObservedCoreAsync(request, logicModule, correlationId, evaluationTimeUtc, cancellationToken);
    }

    public async Task<ObservedExecutionResult> ExecuteObservedAsync(ExecutionRequestedV3 request,
        string correlationId, DateTimeOffset evaluationTimeUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Execution);
        ArgumentNullException.ThrowIfNull(request.Package);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (request.Execution.Assets is null || request.Execution.Inputs is null || request.Execution.Assets.Count == 0)
            throw new WellSiteAutoPilotException("EXECUTION_REQUEST_INVALID", FailureKind.Validation,
                "The hosted execution requires a valid Asset/input snapshot.");
        if (packageHost is null)
            throw new WellSiteAutoPilotException("MODULE_PACKAGE_HOST_UNAVAILABLE", FailureKind.DependencyUnavailable,
                "The worker cannot host this package.");
        await using var loaded = await packageHost.LoadAsync(request.Execution.ModuleId, request.Execution.ModuleVersion,
            request.Package.Sha256, request.Package.ReviewedManifestJson, cancellationToken);
        return await ExecuteObservedCoreAsync(request.Execution, loaded.Module, correlationId, evaluationTimeUtc, cancellationToken);
    }

    private async Task<ObservedExecutionResult> ExecuteObservedCoreAsync(ExecutionRequestedV2 request,
        ILogicModuleV1 logicModule, string correlationId, DateTimeOffset evaluationTimeUtc, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (request.Assets.Count == 0) throw new InvalidOperationException("Execution request must contain at least one Asset binding.");

        using var readLimiter = new SemaphoreSlim(_maximumConcurrentInputReads);
        var inputTasks = request.Inputs
            .Select(async binding =>
            {
                await readLimiter.WaitAsync(cancellationToken);
                try
                {
                    return await ReadInputAsync(binding, evaluationTimeUtc, cancellationToken);
                }
                finally
                {
                    readLimiter.Release();
                }
            })
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

        var result = await logicModule.ExecuteAsync(context, cancellationToken);
        EnsureModuleSucceeded(result);
        return new ObservedExecutionResult(result, inputs);
    }

    private async Task<LogicInputValue> ReadInputAsync(
        ExecutionInputBindingV2 binding,
        DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(binding.Access, "Current", StringComparison.Ordinal))
        {
            throw InputFailure("EXECUTION_INPUT_ACCESS_UNSUPPORTED");
        }

        var value = await gatewayDataClient.GetCurrentAsync(
            binding.ProviderId,
            binding.ProviderAssetExternalId,
            binding.Quantity,
            cancellationToken);

        if (!string.Equals(value.Quantity, binding.Quantity, StringComparison.OrdinalIgnoreCase) ||
            (binding.CanonicalUnit is not null &&
             !string.Equals(value.Unit, binding.CanonicalUnit, StringComparison.OrdinalIgnoreCase)))
        {
            throw InputFailure("EXECUTION_INPUT_QUANTITY_MISMATCH");
        }

        if (!string.Equals(value.Quality, "Good", StringComparison.OrdinalIgnoreCase) &&
            !(binding.AllowUncertainQuality && string.Equals(value.Quality, "Uncertain", StringComparison.OrdinalIgnoreCase)))
        {
            throw InputFailure("EXECUTION_INPUT_QUALITY_REJECTED");
        }

        if (value.TimestampUtc > evaluationTimeUtc.AddSeconds(30))
        {
            throw InputFailure("EXECUTION_INPUT_FUTURE");
        }

        if (binding.MaximumAgeSeconds is > 0 &&
            evaluationTimeUtc - value.TimestampUtc >
            TimeSpan.FromSeconds(binding.MaximumAgeSeconds.Value))
        {
            throw InputFailure("EXECUTION_INPUT_STALE");
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
    private static void EnsureModuleSucceeded(LogicModuleExecutionResult? result)
    {
        if (result is null)
        {
            throw new WellSiteAutoPilotException("MODULE_RESULT_INVALID", FailureKind.Validation,
                "The Logic Module returned an invalid result.");
        }

        if (result.FailureCode is not { } code) return;

        if (code.Length is < 1 or > 128 || !char.IsAsciiLetterUpper(code[0]) ||
            code.Any(character => !char.IsAsciiLetterUpper(character) &&
                !char.IsAsciiDigit(character) && character != '_'))
        {
            throw new WellSiteAutoPilotException("MODULE_RESULT_INVALID", FailureKind.Validation,
                "The Logic Module returned an invalid failure code.");
        }

        // Both consumers already publish ExecutionFailed when the engine throws.
        // No algorithm result, recommendation, intent or state update escapes.
        throw new WellSiteAutoPilotException(code, FailureKind.Validation,
            "The Logic Module could not complete its evaluation.");
    }

    private static WellSiteAutoPilotException InputFailure(string code) =>
        new(code, FailureKind.Validation, "Execution input did not satisfy its declared requirements.");

}

public sealed record ObservedExecutionResult(LogicModuleExecutionResult Result,
    IReadOnlyCollection<LogicInputValue> InputObservations);
