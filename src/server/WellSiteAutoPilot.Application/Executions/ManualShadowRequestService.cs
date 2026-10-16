using System.Security.Cryptography;
using System.Text;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Executions;

public sealed class ManualShadowRequestService(
    IExecutionRepository repository, ConfiguredLogicService configuredLogicService, TimeProvider timeProvider)
{
    public async Task<ExecutionRecord> RequestAsync(Guid configurationId, Guid revisionId,
        Guid requestId, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (requestId == Guid.Empty || actor.UserId is not { } userId || userId == Guid.Empty)
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed,
                FailureKind.Validation, "A request identifier and authenticated user are required.");
        // Stable, versioned namespace + canonical IDs: the same user's key can
        // only refer to one execution, including after an ambiguous response.
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"wsa:manual-shadow:v1:{userId:D}:{requestId:D}"));
        var executionId = new Guid(digest.AsSpan(0, 16));
        var previous = await repository.GetAsync(executionId, cancellationToken);
        if (previous is not null) return Match(previous, configurationId, revisionId);
        var command = await configuredLogicService.PrepareManualShadowAsync(configurationId, revisionId, cancellationToken);
        var execution = ExecutionService.CreateConfiguredRequest(command,
            actor.CorrelationId ?? executionId.ToString("D"), timeProvider.GetUtcNow(), executionId);
        return await repository.AddManualConfiguredRequestedAsync(execution, command, actor, cancellationToken);
    }

    public static ExecutionRecord Match(ExecutionRecord previous, Guid configurationId, Guid revisionId)
    {
        if (previous.LogicInstanceId != configurationId || previous.ConfigurationRevisionId != revisionId ||
            previous.RequestContractVersion != 2 || previous.Trigger != ExecutionTriggerKind.Manual || previous.Mode != ExecutionMode.Shadow)
            throw new WellSiteAutoPilotException("EXECUTION_REQUEST_KEY_CONFLICT", FailureKind.Conflict,
                "The request key is already bound to a different request.");
        return previous;
    }
}
