using System.Text.Json;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Recommendations;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Application.Recommendations;

public sealed class RecommendationMaterializer(
    IExecutionRepository executionRepository,
    IRecommendationRepository recommendationRepository,
    IConfiguredLogicRepository configuredLogicRepository,
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

        // Shadow only observes its intents. Recommendation proposes them to an operator; Autonomous hands them to the gate.
        if (!ExecutionModePolicy.MaterializesControlIntents(execution.Mode))
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

        var controlIntents = result.ControlIntents ?? [];
        // Advisory drafts are proposals for a person to read. Only Recommendation mode has a person to read them:
        // Autonomous acts on control intents alone, so it never turns advice into something the gate could see.
        var advisoryIntents = execution.Mode == ExecutionMode.Recommendation
            ? result.Recommendations ?? []
            : [];
        if (controlIntents.Count == 0 && advisoryIntents.Count == 0)
        {
            return 0;
        }

        // A control intent can only name a command the module declared, so no recommendation is created that no policy
        // could ever permit. The revision the execution ran with is the one that counts, not whatever is live now.
        IReadOnlyList<DeclaredModuleCommand> declaredCommands = [];
        if (controlIntents.Count > 0)
        {
            var definition = await configuredLogicRepository.GetAsync(execution.LogicInstanceId, cancellationToken);
            var revision = definition?.Revisions.FirstOrDefault(item => item.Id == execution.ConfigurationRevisionId);
            declaredCommands = ModuleCommandDeclarations.Read(revision?.ModuleManifestJson) ??
                throw new WellSiteAutoPilotException(
                    "RECOMMENDATION_MODULE_UNREADABLE",
                    FailureKind.Validation,
                    "The module manifest of the revision that ran could not be read, so its control commands cannot be checked.");
        }

        var assetScope = (await executionRepository.GetAssetScopeAsync(
                execution.Id,
                cancellationToken))
            .ToHashSet();

        var createdAtUtc = execution.CompletedAtUtc ??
                           timeProvider.GetUtcNow();

        var recommendations = controlIntents
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
                if (string.Equals(command, RecommendationCommands.Advisory, StringComparison.OrdinalIgnoreCase))
                {
                    throw ValidationFailure("The advisory command is reserved and cannot be used as a control command.");
                }

                if (!declaredCommands.Any(item => string.Equals(item.Command, command, StringComparison.Ordinal)))
                {
                    throw new WellSiteAutoPilotException(
                        "RECOMMENDATION_COMMAND_NOT_DECLARED",
                        FailureKind.Validation,
                        $"Control intent command '{command}' is not declared by the module's commandRequirements.");
                }

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
            .ToList();

        foreach (var advisory in advisoryIntents)
        {
            if (!assetScope.Contains(advisory.AssetId))
            {
                throw new WellSiteAutoPilotException(
                    "RECOMMENDATION_ASSET_OUT_OF_SCOPE",
                    FailureKind.Validation,
                    $"Recommendation Asset '{advisory.AssetId}' is outside the source execution Asset scope.");
            }

            var code = RequireText(advisory.Code, "Recommendation code is required.");
            RequireText(advisory.Title, "Recommendation title is required.");
            recommendations.Add(new RecommendationRecord(
                Guid.NewGuid(),
                execution.Id,
                recommendations.Count,
                execution.LogicInstanceId,
                execution.ConfigurationRevisionId,
                execution.ModuleId,
                execution.ModuleVersion,
                advisory.AssetId,
                code,
                RecommendationCommands.Advisory,
                string.IsNullOrWhiteSpace(advisory.Quantity) ? null : advisory.Quantity.Trim(),
                advisory.SuggestedValue,
                string.IsNullOrWhiteSpace(advisory.Unit) ? null : advisory.Unit.Trim(),
                code,
                JsonSerializer.Serialize(advisory, SerializerOptions),
                RecommendationStatus.Created,
                createdAtUtc));
        }

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
