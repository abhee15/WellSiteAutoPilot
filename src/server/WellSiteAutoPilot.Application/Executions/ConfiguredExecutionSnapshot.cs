using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;

namespace WellSiteAutoPilot.Application.Executions;

public static class ConfiguredExecutionSnapshot
{
    public static ConfiguredShadowExecutionCommand Create(
        ConfiguredLogicDefinition configuredLogic,
        ConfiguredLogicRevision revision,
        ExecutionTriggerKind trigger,
        DateTimeOffset? scheduledForUtc)
    {
        var manifest = ConfiguredLogicService.DeserializeAndValidateManifest(
            revision.ModuleManifestJson);

        if (!string.Equals(
                manifest.ModuleId,
                revision.ModuleId,
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.Version,
                revision.ModuleVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Configured Logic revision manifest identity does not match its module identity.");
        }

        // Schema declarations may be reviewed/stored now, but never lower them into
        // the scalar execution contract while structured transport is unavailable.
        if (manifest.ManifestVersion != 1)
            throw new WellSiteAutoPilot.Failures.WellSiteAutoPilotException("MODULE_CONTRACT_UNSUPPORTED",
                WellSiteAutoPilot.Failures.FailureKind.Validation,
                "This deployment does not yet execute structured modules.");

        var requirements = manifest.DataRequirements.ToDictionary(
            item => item.Id,
            StringComparer.OrdinalIgnoreCase);

        var inputs = revision.DataBindings
            .Select(binding =>
            {
                if (!requirements.TryGetValue(
                        binding.RequirementId,
                        out var requirement))
                {
                    throw new InvalidOperationException(
                        $"Configured Logic data binding '{binding.RequirementId}' is not present in the manifest snapshot.");
                }

                return new ConfiguredExecutionInputCommand(
                    binding.RequirementId,
                    binding.AssetId,
                    binding.ProviderId,
                    binding.ProviderAssetExternalId,
                    requirement.Quantity,
                    requirement.Access,
                    requirement.CanonicalUnit,
                    requirement.MaximumAgeSeconds,
                    requirement.AllowUncertainQuality,
                    binding.ProviderMappingJson);
            })
            .ToArray();

        foreach (var requirement in manifest.DataRequirements)
        {
            if (!inputs.Any(
                    item => string.Equals(
                        item.RequirementId,
                        requirement.Id,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Configured Logic data requirement '{requirement.Id}' has no execution input binding.");
            }
        }

        return new ConfiguredShadowExecutionCommand(
            configuredLogic.Id,
            revision.Id,
            revision.ModuleId,
            revision.ModuleVersion,
            revision.ParametersJson,
            revision.AssetBindings
                .Select(binding => new ConfiguredExecutionAssetCommand(
                    binding.Role,
                    binding.AssetId,
                    binding.ParameterOverridesJson))
                .ToArray(),
            inputs,
            trigger,
            scheduledForUtc);
    }

}
