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
