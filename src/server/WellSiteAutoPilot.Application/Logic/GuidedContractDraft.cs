using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>
/// Validation for contract-only guided drafts. The saved model is the existing versioned V2 manifest
/// (roles, typed inputs, typed outputs, parameter schema), so there is no second schema. A contract-only
/// draft carries a server-owned placeholder entry point and can never be packaged or executed.
/// </summary>
public static class GuidedContractDraft
{
    public const string UnsetEntryPoint = "contract.unset";

    public static async Task ValidateAsync(LogicModuleManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.ManifestVersion != 2 || !string.Equals(manifest.EntryPoint, UnsetEntryPoint, StringComparison.Ordinal))
            throw new WellSiteAutoPilotException("GUIDED_CONTRACT_INVALID", FailureKind.Validation,
                "A contract-only draft must use manifest version 2 and declare no runnable entry point.");
        // Authoring never grants control authority; command declarations belong to reviewed, executable modules.
        if (manifest.CommandRequirements is not { Count: 0 })
            throw new WellSiteAutoPilotException("GUIDED_CONTRACT_COMMANDS_UNSUPPORTED", FailureKind.Validation,
                "A contract-only draft cannot declare commands.");
        // A draft is saved while it is being built, so zero ports are allowed here but never in an executable module.
        await LogicModuleManifestValidator.ValidateSchemasAsync(manifest, allowEmptyPorts: true, cancellationToken);
    }
}
