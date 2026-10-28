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
        if (loaded.StructuredModule is { } structured)
            return await ExecuteStructuredAsync(request.Execution, structured, correlationId, evaluationTimeUtc, cancellationToken);
        return await ExecuteObservedCoreAsync(request.Execution, loaded.Module, correlationId, evaluationTimeUtc, cancellationToken);
    }

    // POC: structured modules read the same authorized Current inputs; array/object inputs travel as JSON.
    // Only scalar structured results are lowered into the existing scalar result contract.
    private async Task<ObservedExecutionResult> ExecuteStructuredAsync(ExecutionRequestedV2 request,
        WellSiteAutoPilot.ModuleSdk.ILogicModuleV2 module, string correlationId, DateTimeOffset evaluationTimeUtc,
        CancellationToken cancellationToken)
    {
        var diagnostics = new ExecutionFailureDiagnostics();
        var structuredInputs = new List<WellSiteAutoPilot.ModuleSdk.StructuredInputValue>();
        var observations = new List<LogicInputValue>();
        try
        {
            foreach (var binding in request.Inputs)
            {
                WellSiteAutoPilot.Integration.Contracts.Providers.EngineeringValue? measurement = null;
                try
                {
                    if (!string.Equals(binding.Access, "Current", StringComparison.Ordinal))
                        throw InputFailure("EXECUTION_INPUT_ACCESS_UNSUPPORTED");
                    var value = await gatewayDataClient.GetCurrentAsync(request.ExecutionId, binding, cancellationToken);
                    measurement = value;
                    var input = new LogicInputValue(binding.RequirementId, binding.AssetId, value.Quantity, value.Value,
                        value.Unit, value.TimestampUtc, value.Quality, value.Source);
                    WellSiteAutoPilot.Application.Logic.LogicModuleInputValidation.Validate(input,
                        new WellSiteAutoPilot.Domain.Logic.LogicDataRequirement(binding.RequirementId, "", binding.Quantity,
                            binding.Access, binding.CanonicalUnit, binding.MaximumAgeSeconds, binding.AllowUncertainQuality),
                        evaluationTimeUtc);
                    var data = value.StructuredJson is null
                        ? System.Text.Json.JsonSerializer.SerializeToElement(value.Value)
                        : System.Text.Json.JsonDocument.Parse(value.StructuredJson).RootElement.Clone();
                    structuredInputs.Add(new(binding.RequirementId, binding.AssetId, value.Quantity,
                        new(value.StructuredSchemaId ?? "number", data), value.StructuredJson is null ? value.Unit : null,
                        value.TimestampUtc, value.Quality, value.Source));
                    observations.Add(input);
                    diagnostics.Record(binding, measurement, "ACCEPTED");
                }
                catch (Exception error)
                {
                    diagnostics.Record(binding, measurement, ExecutionFailureDiagnostics.FailureCode(error));
                    throw;
                }
            }
            var context = new WellSiteAutoPilot.ModuleSdk.LogicModuleExecutionContextV2(request.ExecutionId, module.Identity,
                correlationId, evaluationTimeUtc, request.ParametersJson,
                request.Assets.Select(asset => new LogicAssetContext(asset.Role, asset.AssetId, asset.ParameterOverridesJson)).ToArray(),
                structuredInputs);
            var result = await module.ExecuteAsync(context, cancellationToken);
            var lowered = new LogicModuleExecutionResult(result.OutcomeCode,
                result.EngineeringResults.Select(item =>
                    item.Value.Data.ValueKind == System.Text.Json.JsonValueKind.Number && item.Value.Data.TryGetDecimal(out var number)
                        ? new EngineeringResultValue(item.Id, item.AssetId, item.Quantity, number, item.Unit ?? "")
                        : throw new WellSiteAutoPilotException("MODULE_STRUCTURED_RESULT_UNSUPPORTED", FailureKind.Validation,
                            "This deployment can only persist scalar results.")).ToArray(),
                result.Recommendations, result.ControlIntents, result.StateUpdates)
            { FailureCode = result.FailureCode };
            EnsureModuleSucceeded(lowered);
            return new ObservedExecutionResult(lowered, observations);
        }
        catch (Exception error)
        {
            diagnostics.Attach(error);
            throw;
        }
    }

    private async Task<ObservedExecutionResult> ExecuteObservedCoreAsync(ExecutionRequestedV2 request,
        ILogicModuleV1 logicModule, string correlationId, DateTimeOffset evaluationTimeUtc, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (request.Assets.Count == 0) throw new InvalidOperationException("Execution request must contain at least one Asset binding.");

        var diagnostics = new ExecutionFailureDiagnostics();
        using var readLimiter = new SemaphoreSlim(_maximumConcurrentInputReads);
        var inputTasks = request.Inputs
            .Select(async binding =>
            {
                await readLimiter.WaitAsync(cancellationToken);
                WellSiteAutoPilot.Integration.Contracts.Providers.EngineeringValue? measurement = null;
                try
                {
                    var input = await ReadInputAsync(request.ExecutionId, binding, evaluationTimeUtc,
                        value => measurement = value, cancellationToken);
                    diagnostics.Record(binding, measurement, "ACCEPTED");
                    return input;
                }
                catch (Exception error)
                {
                    diagnostics.Record(binding, measurement, ExecutionFailureDiagnostics.FailureCode(error));
                    throw;
                }
                finally
                {
                    readLimiter.Release();
                }
            })
            .ToArray();

        try
        {
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
        catch (Exception error)
        {
            diagnostics.Attach(error);
            throw;
        }
    }

    private async Task<LogicInputValue> ReadInputAsync(
        Guid executionId,
        ExecutionInputBindingV2 binding,
        DateTimeOffset evaluationTimeUtc,
        Action<WellSiteAutoPilot.Integration.Contracts.Providers.EngineeringValue> observe,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(binding.Access, "Current", StringComparison.Ordinal))
        {
            throw InputFailure("EXECUTION_INPUT_ACCESS_UNSUPPORTED");
        }

        var value = await gatewayDataClient.GetCurrentAsync(executionId, binding, cancellationToken);
        observe(value);
        if (value.TimestampKind == "UnverifiedScan") throw InputFailure("EXECUTION_INPUT_TIMESTAMP_UNVERIFIED");
        var input = new LogicInputValue(binding.RequirementId, binding.AssetId, value.Quantity, value.Value,
            value.Unit, value.TimestampUtc, value.Quality, value.Source);
        WellSiteAutoPilot.Application.Logic.LogicModuleInputValidation.Validate(input,
            new WellSiteAutoPilot.Domain.Logic.LogicDataRequirement(binding.RequirementId, "", binding.Quantity, binding.Access,
                binding.CanonicalUnit, binding.MaximumAgeSeconds, binding.AllowUncertainQuality), evaluationTimeUtc);
        return input;
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
