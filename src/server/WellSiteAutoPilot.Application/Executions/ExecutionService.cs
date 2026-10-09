using System.Text.Json;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Executions;

public sealed class ExecutionService(
    IExecutionRepository repository,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<ExecutionRecord> RequestShadowAsync(
        ShadowExecutionCommand command,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        Validate(command);

        var requestedAtUtc = timeProvider.GetUtcNow();
        var execution = new ExecutionRecord(
            Guid.NewGuid(),
            command.LogicInstanceId,
            command.ModuleId.Trim(),
            command.ModuleVersion.Trim(),
            command.ConfigurationRevisionId,
            command.AssetId,
            command.AssetExternalId.Trim(),
            command.Quantity.Trim(),
            ExecutionMode.Shadow,
            ExecutionStatus.Requested,
            correlationId.Trim(),
            requestedAtUtc);

        await repository.AddRequestedAsync(execution, cancellationToken);
        return execution;
    }

    public async Task<ExecutionRecord> RequestConfiguredShadowAsync(
        ConfiguredShadowExecutionCommand command,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var normalized = ValidateAndNormalize(command);
        var requestedAtUtc = timeProvider.GetUtcNow();
        var execution = new ExecutionRecord(
            Guid.NewGuid(),
            normalized.ConfiguredLogicId,
            normalized.ModuleId,
            normalized.ModuleVersion,
            normalized.ConfigurationRevisionId,
            null,
            null,
            null,
            ExecutionMode.Shadow,
            ExecutionStatus.Requested,
            correlationId.Trim(),
            requestedAtUtc,
            2,
            normalized.Trigger,
            normalized.ScheduledForUtc,
            JsonSerializer.Serialize(normalized, SerializerOptions));

        await repository.AddConfiguredRequestedAsync(
            execution,
            normalized,
            cancellationToken);

        return execution;
    }

    public async Task<ExecutionRecord> GetRequiredAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        var execution = await repository.GetAsync(executionId, cancellationToken);
        return execution ?? throw new WellSiteAutoPilotException(
            "EXECUTION_NOT_FOUND",
            FailureKind.NotFound,
            "The requested execution was not found.");
    }

    public async Task<ExecutionRecord> GetRequiredInScopeAsync(
        Guid executionId,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedAssetIds);

        return await repository.GetInScopeAsync(
                   executionId,
                   allowedAssetIds,
                   cancellationToken) ??
               throw new WellSiteAutoPilotException(
                   "EXECUTION_NOT_FOUND",
                   FailureKind.NotFound,
                   "The requested execution was not found.");
    }

    public Task<IReadOnlyCollection<ExecutionRecord>> ListAsync(
        string? status,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Execution list limit must be between 1 and 500.");
        }

        var parsedStatus = ParseStatus(status);
        return repository.ListAsync(parsedStatus, limit, cancellationToken);
    }

    public Task<IReadOnlyCollection<ExecutionRecord>> ListInScopeAsync(
        string? status,
        int limit,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedAssetIds);

        if (limit is < 1 or > 500)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Execution list limit must be between 1 and 500.");
        }

        var parsedStatus = ParseStatus(status);
        return repository.ListInScopeAsync(
            parsedStatus,
            limit,
            allowedAssetIds,
            cancellationToken);
    }

    public Task<IReadOnlyCollection<Guid>> GetAssetScopeAsync(
        Guid executionId,
        CancellationToken cancellationToken = default) =>
        repository.GetAssetScopeAsync(
            executionId,
            cancellationToken);

    private static ExecutionStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        if (!Enum.TryParse<ExecutionStatus>(
                status.Trim(),
                ignoreCase: true,
                out var value))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Execution status is not recognized.");
        }

        return value;
    }

    private static void Validate(ShadowExecutionCommand command)
    {
        if (command.LogicInstanceId == Guid.Empty ||
            command.ConfigurationRevisionId == Guid.Empty ||
            command.AssetId == Guid.Empty)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Logic instance, configuration revision, and asset identifiers are required.");
        }

        if (string.IsNullOrWhiteSpace(command.ModuleId) ||
            string.IsNullOrWhiteSpace(command.ModuleVersion) ||
            string.IsNullOrWhiteSpace(command.AssetExternalId) ||
            string.IsNullOrWhiteSpace(command.Quantity))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Module, asset mapping, and quantity are required.");
        }
    }

    private static ConfiguredShadowExecutionCommand ValidateAndNormalize(
        ConfiguredShadowExecutionCommand command)
    {
        if (command.ConfiguredLogicId == Guid.Empty ||
            command.ConfigurationRevisionId == Guid.Empty)
        {
            throw ValidationFailure(
                "Configured Logic and configuration revision identifiers are required.");
        }

        var moduleId = RequireText(command.ModuleId, "Module ID is required.");
        var moduleVersion = RequireText(command.ModuleVersion, "Module version is required.");
        ValidateJson(command.ParametersJson, "Configured execution parameters must be valid JSON.");

        if (command.Assets is null || command.Assets.Count == 0)
        {
            throw ValidationFailure(
                "Configured execution requires at least one Asset binding.");
        }

        if (command.Inputs is null)
        {
            throw ValidationFailure("Configured execution inputs are required.");
        }

        var normalizedAssets = command.Assets
            .Select(asset =>
            {
                if (asset.AssetId == Guid.Empty)
                {
                    throw ValidationFailure("Configured execution Asset ID is required.");
                }

                ValidateJson(
                    asset.ParameterOverridesJson,
                    "Configured execution Asset parameter overrides must be valid JSON.");

                return new ConfiguredExecutionAssetCommand(
                    RequireText(asset.Role, "Configured execution Asset role is required."),
                    asset.AssetId,
                    NormalizeJson(asset.ParameterOverridesJson));
            })
            .ToArray();

        var knownAssets = normalizedAssets
            .Select(asset => asset.AssetId)
            .ToHashSet();

        var normalizedInputs = command.Inputs
            .Select(input =>
            {
                if (input.AssetId == Guid.Empty || !knownAssets.Contains(input.AssetId))
                {
                    throw ValidationFailure(
                        "Configured execution input must reference a bound Asset.");
                }

                if (input.MaximumAgeSeconds is <= 0)
                {
                    throw ValidationFailure(
                        "Maximum input age must be greater than zero when specified.");
                }

                ValidateJson(
                    input.ProviderMappingJson,
                    "Configured execution provider mapping must be valid JSON.");

                return new ConfiguredExecutionInputCommand(
                    RequireText(input.RequirementId, "Input requirement ID is required."),
                    input.AssetId,
                    RequireText(input.ProviderId, "Input provider ID is required."),
                    RequireText(
                        input.ProviderAssetExternalId,
                        "Provider Asset external ID is required."),
                    RequireText(input.Quantity, "Input quantity is required."),
                    RequireText(input.Access, "Input access mode is required."),
                    string.IsNullOrWhiteSpace(input.CanonicalUnit)
                        ? null
                        : input.CanonicalUnit.Trim(),
                    input.MaximumAgeSeconds,
                    input.AllowUncertainQuality,
                    NormalizeJson(input.ProviderMappingJson));
            })
            .ToArray();

        DateTimeOffset? scheduledForUtc = command.ScheduledForUtc is null
            ? null
            : NormalizeUtcTimestamp(command.ScheduledForUtc.Value);

        if (command.Trigger == ExecutionTriggerKind.Scheduled && scheduledForUtc is null)
        {
            throw ValidationFailure(
                "Scheduled executions require the scheduled occurrence time.");
        }

        if (command.Trigger != ExecutionTriggerKind.Scheduled && scheduledForUtc is not null)
        {
            throw ValidationFailure(
                "Only scheduled executions can specify a scheduled occurrence time.");
        }

        return new ConfiguredShadowExecutionCommand(
            command.ConfiguredLogicId,
            command.ConfigurationRevisionId,
            moduleId,
            moduleVersion,
            NormalizeJson(command.ParametersJson),
            normalizedAssets,
            normalizedInputs,
            command.Trigger,
            scheduledForUtc);
    }

    private static DateTimeOffset NormalizeUtcTimestamp(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticks = utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static string RequireText(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ValidationFailure(message);
        }

        return value.Trim();
    }

    private static void ValidateJson(string? value, string message)
    {
        try
        {
            using var _ = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(value) ? "{}" : value);
        }
        catch (JsonException exception)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                message,
                innerException: exception);
        }
    }

    private static string NormalizeJson(string? value)
    {
        using var document = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(value) ? "{}" : value);

        return JsonSerializer.Serialize(
            document.RootElement,
            SerializerOptions);
    }

    private static WellSiteAutoPilotException ValidationFailure(string message) =>
        new(
            FailureCodes.ValidationFailed,
            FailureKind.Validation,
            message);
}
