using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

public enum LogicModuleApprovalAction
{
    Approve = 0,
    Retire = 1
}

/// <summary>The approval state a module moves to; absent when nothing changes.</summary>
public sealed record LogicModuleApprovalChange(LogicModuleTrustStatus TrustStatus, bool IsEnabled);

/// <summary>
/// The only rules for moving an installed module between "not approved",
/// "approved" and "retired". Registration never changes these states, and a
/// retired version is never brought back: a fixed module is a new version.
/// </summary>
public static class LogicModuleApproval
{
    public const int MaximumReasonLength = 500;

    public static LogicModuleApprovalChange? Decide(InstalledLogicModule current, LogicModuleApprovalAction action)
    {
        ArgumentNullException.ThrowIfNull(current);
        var neverApproved = current.TrustStatus == LogicModuleTrustStatus.Untrusted;
        switch (action)
        {
            case LogicModuleApprovalAction.Approve:
                if (current.IsEnabled)
                {
                    return null;
                }

                if (neverApproved)
                {
                    return new LogicModuleApprovalChange(LogicModuleTrustStatus.CustomerApproved, true);
                }

                throw new WellSiteAutoPilotException(
                    "MODULE_RETIRED",
                    FailureKind.Conflict,
                    "This version was retired and cannot be approved again. Register a new version instead.");
            case LogicModuleApprovalAction.Retire:
                if (current.IsEnabled)
                {
                    return new LogicModuleApprovalChange(current.TrustStatus, false);
                }

                if (neverApproved)
                {
                    throw new WellSiteAutoPilotException(
                        "MODULE_NOT_APPROVED",
                        FailureKind.Conflict,
                        "This version was never approved, so there is nothing to retire.");
                }

                return null;
            default:
                throw new WellSiteAutoPilotException(
                    FailureCodes.ValidationFailed,
                    FailureKind.Validation,
                    "Logic Module approval action is not recognized.");
        }
    }

    public static string ValidateReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new WellSiteAutoPilotException(
                "MODULE_APPROVAL_REASON_REQUIRED",
                FailureKind.Validation,
                "A reason is required.");
        }

        if (trimmed.Length > MaximumReasonLength || trimmed.Any(char.IsControl))
        {
            throw new WellSiteAutoPilotException(
                "MODULE_APPROVAL_REASON_INVALID",
                FailureKind.Validation,
                $"The reason must be plain text of at most {MaximumReasonLength} characters.");
        }

        return trimmed;
    }
}

public interface ILogicModuleApprovalRepository
{
    Task<InstalledLogicModule> ChangeApprovalAuthorizedAsync(
        string moduleId,
        string version,
        LogicModuleApprovalAction action,
        string reason,
        AccessSubject subject,
        SecurityActorContext actor,
        CancellationToken cancellationToken = default);
}

public sealed class LogicModuleApprovalService(ILogicModuleApprovalRepository repository)
{
    public Task<InstalledLogicModule> ApproveAsync(
        string moduleId, string version, string? reason, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default) =>
        ChangeAsync(moduleId, version, LogicModuleApprovalAction.Approve, reason, subject, actor, cancellationToken);

    public Task<InstalledLogicModule> RetireAsync(
        string moduleId, string version, string? reason, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default) =>
        ChangeAsync(moduleId, version, LogicModuleApprovalAction.Retire, reason, subject, actor, cancellationToken);

    private async Task<InstalledLogicModule> ChangeAsync(
        string moduleId, string version, LogicModuleApprovalAction action, string? reason,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(version))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Module ID and version are required.");
        }

        return await repository.ChangeApprovalAuthorizedAsync(
            moduleId.Trim(), version.Trim(), action, LogicModuleApproval.ValidateReason(reason),
            subject, actor, cancellationToken);
    }
}
