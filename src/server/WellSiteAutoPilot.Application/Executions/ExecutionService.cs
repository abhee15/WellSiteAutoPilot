using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Executions;

public sealed class ExecutionService(
    IExecutionRepository repository,
    TimeProvider timeProvider)
{
    public async Task<ExecutionRecord> RequestShadowAsync(
        ShadowExecutionCommand command,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        Validate(command);

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
            correlationId,
            timeProvider.GetUtcNow());

        await repository.AddRequestedAsync(execution, cancellationToken);
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

        ExecutionStatus? parsedStatus = null;

        if (!string.IsNullOrWhiteSpace(status))
        {
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

            parsedStatus = value;
        }

        return repository.ListAsync(parsedStatus, limit, cancellationToken);
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
}
