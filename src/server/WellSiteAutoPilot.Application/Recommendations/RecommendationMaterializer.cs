using System.Text.Json;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Recommendations;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Application.Recommendations;

public sealed class RecommendationMaterializer(
    IExecutionRepository executionRepository,
    IRecommendationRepository recommendationRepository,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<int> MaterializeAsync(
        Guid executionId,
        string outputJson,
        CancellationToken cancellationToken = default)
    {
        if (executionId == Guid.Empty)
        {
            throw ValidationFailure("Execution ID is required.");
        }

        var execution = await executionRepository.GetAsync(
            executionId,
            cancellationToken) ??
            throw new WellSiteAutoPilotException(
                "EXECUTION_NOT_FOUND",
                FailureKind.NotFound,
                "The source execution was not found.");

        if (execution.Mode != ExecutionMode.Recommendation)
        {
            return 0;
        }

        if (execution.Status != ExecutionStatus.Completed)
        {
            throw new WellSiteAutoPilotException(
                "RECOMMENDATION_SOURCE_EXECUTION_NOT_COMPLETED",
                FailureKind.Conflict,
                "Recommendations can only be materialized from a completed execution.");
        }

        LogicModuleExecutionResult result;

        try
        {
            result = JsonSerializer.Deserialize<LogicModuleExecutionResult>(
                outputJson,
                SerializerOptions) ??
                throw new JsonException("Execution output was empty.");
        }
        catch (JsonException exception)
        {
            throw new WellSiteAutoPilotException(
                "RECOMMENDATION_OUTPUT_INVALID",
                FailureKind.Validation,
                "Completed execution output could not be read as a Logic Module result.",
                innerException: exception);
        }

        if (result.ControlIntents is null ||
            result.ControlIntents.Count == 0)
        {
            return 0;
        }

        var assetScope = (await executionRepository.GetAssetScopeAsync(
                execution.Id,
                cancellationToken))
            .ToHashSet();

        var createdAtUtc = execution.CompletedAtUtc ??
                           timeProvider.GetUtcNow();

        var recommendations = result.ControlIntents
            .Select((intent, index) =>
            {
                if (!assetScope.Contains(intent.AssetId))
                {
                    throw new WellSiteAutoPilotException(
                        "RECOMMENDATION_ASSET_OUT_OF_SCOPE",
                        FailureKind.Validation,
                        $"Control intent Asset '{intent.AssetId}' is outside the source execution Asset scope.");
                }

                var code = RequireText(
                    intent.Code,
                    "Control intent code is required.");
                var command = RequireText(
                    intent.Command,
                    "Control intent command is required.");
                var reasonCode = RequireText(
                    intent.ReasonCode,
                    "Control intent reason code is required.");

                return new RecommendationRecord(
                    Guid.NewGuid(),
                    execution.Id,
                    index,
                    execution.LogicInstanceId,
                    execution.ConfigurationRevisionId,
                    execution.ModuleId,
                    execution.ModuleVersion,
                    intent.AssetId,
                    code,
                    command,
                    string.IsNullOrWhiteSpace(intent.Quantity)
                        ? null
                        : intent.Quantity.Trim(),
                    intent.RequestedValue,
                    string.IsNullOrWhiteSpace(intent.Unit)
                        ? null
                        : intent.Unit.Trim(),
                    reasonCode,
                    JsonSerializer.Serialize(intent, SerializerOptions),
                    RecommendationStatus.Created,
                    createdAtUtc);
            })
            .ToArray();

        return await recommendationRepository.AddMissingAsync(
            recommendations,
            cancellationToken);
    }

    private static string RequireText(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ValidationFailure(message);
        }

        return value.Trim();
    }

    private static WellSiteAutoPilotException ValidationFailure(
        string message) =>
        new(
            FailureCodes.ValidationFailed,
            FailureKind.Validation,
            message);
}
