using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.ConfiguredLogic;

public sealed class ConfiguredLogicService(
    IConfiguredLogicRepository repository,
    IAssetRepository assetRepository,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions ManifestSerializerOptions = CreateManifestSerializerOptions();

    public async Task<ConfiguredLogicDefinition> CreateAsync(
        CreateConfiguredLogicCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = RequireText(command.Name, "Configured Logic name is required.");
        var manifest = DeserializeAndValidateManifest(command.ModuleManifestJson);
        ValidateJson(command.ParametersJson, "Configured Logic parameters must be valid JSON.");

        var assetBindings = await ValidateAssetBindingsAsync(
            manifest,
            command.AssetBindings,
            cancellationToken);
        var dataBindings = ValidateDataBindings(
            manifest,
            assetBindings,
            command.DataBindings);
        var schedule = ValidateSchedule(manifest, command.Schedule);

        var now = timeProvider.GetUtcNow();
        var configuredLogicId = Guid.NewGuid();
        var revision = new ConfiguredLogicRevision(
            Guid.NewGuid(),
            configuredLogicId,
            1,
            manifest.ModuleId,
            manifest.Version,
            NormalizeJson(command.ModuleManifestJson),
            ExecutionMode.Shadow,
            NormalizeJson(command.ParametersJson),
            ConfiguredLogicRevisionStatus.Draft,
            now,
            null,
            null,
            schedule,
            assetBindings,
            dataBindings);

        var configuredLogic = new ConfiguredLogicDefinition(
            configuredLogicId,
            name,
            null,
            now,
            [revision]);

        await repository.AddAsync(configuredLogic, cancellationToken);
        return configuredLogic;
    }

    public async Task<ConfiguredLogicRevision> CreateRevisionAsync(
        Guid configuredLogicId,
        CreateConfiguredLogicRevisionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await GetRequiredAsync(configuredLogicId, cancellationToken);
        var manifest = DeserializeAndValidateManifest(command.ModuleManifestJson);
        ValidateJson(command.ParametersJson, "Configured Logic parameters must be valid JSON.");

        var assetBindings = await ValidateAssetBindingsAsync(
            manifest,
            command.AssetBindings,
            cancellationToken);
        var dataBindings = ValidateDataBindings(
            manifest,
            assetBindings,
            command.DataBindings);
        var schedule = ValidateSchedule(manifest, command.Schedule);

        var revision = new ConfiguredLogicRevision(
            Guid.NewGuid(),
            configuredLogicId,
            existing.Revisions.Count == 0
                ? 1
                : existing.Revisions.Max(item => item.RevisionNumber) + 1,
            manifest.ModuleId,
            manifest.Version,
            NormalizeJson(command.ModuleManifestJson),
            ExecutionMode.Shadow,
            NormalizeJson(command.ParametersJson),
            ConfiguredLogicRevisionStatus.Draft,
            timeProvider.GetUtcNow(),
            null,
            null,
            schedule,
            assetBindings,
            dataBindings);

        await repository.AddRevisionAsync(revision, cancellationToken);
        return revision;
    }

    public async Task<ConfiguredLogicRevision> ValidateRevisionAsync(
        Guid configuredLogicId,
        Guid revisionId,
        CancellationToken cancellationToken = default)
    {
        var configuredLogic = await GetRequiredAsync(configuredLogicId, cancellationToken);
        var revision = GetRequiredRevision(configuredLogic, revisionId);

        if (revision.Status != ConfiguredLogicRevisionStatus.Draft)
        {
            throw new WellSiteAutoPilotException(
                "CONFIGURED_LOGIC_REVISION_NOT_DRAFT",
                FailureKind.Conflict,
                "Only Draft Configured Logic revisions can be validated.");
        }

        var manifest = DeserializeAndValidateManifest(revision.ModuleManifestJson);
        var assetBindings = await ValidateAssetBindingsAsync(
            manifest,
            revision.AssetBindings
                .Select(item => new ConfiguredLogicAssetBindingCommand(
                    item.Role,
                    item.AssetId,
                    item.ParameterOverridesJson))
                .ToArray(),
            cancellationToken);

        ValidateDataBindings(
            manifest,
            assetBindings,
            revision.DataBindings
                .Select(item => new ConfiguredLogicDataBindingCommand(
                    item.RequirementId,
                    item.AssetId,
                    item.ProviderId,
                    item.ProviderAssetExternalId,
                    item.ProviderMappingJson))
                .ToArray());

        ValidateSchedule(
            manifest,
            revision.Schedule is null
                ? null
                : new ConfiguredLogicScheduleCommand(
                    revision.Schedule.Enabled,
                    revision.Schedule.CadenceSeconds,
                    revision.Schedule.StartAtUtc,
                    revision.Schedule.TimeZoneId));

        await repository.SetRevisionValidatedAsync(
            configuredLogicId,
            revisionId,
            timeProvider.GetUtcNow(),
            cancellationToken);

        var updated = await GetRequiredAsync(configuredLogicId, cancellationToken);
        return GetRequiredRevision(updated, revisionId);
    }

    public async Task<ConfiguredLogicRevision> ActivateRevisionAsync(
        Guid configuredLogicId,
        Guid revisionId,
        CancellationToken cancellationToken = default)
    {
        var configuredLogic = await GetRequiredAsync(configuredLogicId, cancellationToken);
        var revision = GetRequiredRevision(configuredLogic, revisionId);

        if (revision.Status != ConfiguredLogicRevisionStatus.Validated)
        {
            throw new WellSiteAutoPilotException(
                "CONFIGURED_LOGIC_REVISION_NOT_VALIDATED",
                FailureKind.Conflict,
                "Only Validated Configured Logic revisions can be activated.");
        }

        await repository.ActivateRevisionAsync(
            configuredLogicId,
            revisionId,
            timeProvider.GetUtcNow(),
            cancellationToken);

        var updated = await GetRequiredAsync(configuredLogicId, cancellationToken);
        return GetRequiredRevision(updated, revisionId);
    }

    public async Task<ConfiguredLogicDefinition> GetRequiredAsync(
        Guid configuredLogicId,
        CancellationToken cancellationToken = default) =>
        await repository.GetAsync(configuredLogicId, cancellationToken) ??
        throw new WellSiteAutoPilotException(
            "CONFIGURED_LOGIC_NOT_FOUND",
            FailureKind.NotFound,
            "The requested Configured Logic was not found.");

    public Task<IReadOnlyCollection<ConfiguredLogicDefinition>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Configured Logic list limit must be between 1 and 500.");
        }

        return repository.ListAsync(limit, cancellationToken);
    }

    private async Task<IReadOnlyCollection<ConfiguredLogicAssetBinding>> ValidateAssetBindingsAsync(
        LogicModuleManifest manifest,
        IReadOnlyCollection<ConfiguredLogicAssetBindingCommand> commands,
        CancellationToken cancellationToken)
    {
        if (commands is null)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Configured Logic asset bindings are required.");
        }

        var requirements = manifest.AssetRequirements
            .ToDictionary(item => item.Role, StringComparer.OrdinalIgnoreCase);

        var normalized = new List<ConfiguredLogicAssetBinding>(commands.Count);
        var uniqueBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in commands)
        {
            var role = RequireText(command.Role, "Configured Logic asset role is required.");

            if (!requirements.TryGetValue(role, out var requirement))
            {
                throw new WellSiteAutoPilotException(
                    "CONFIGURED_LOGIC_ASSET_ROLE_UNKNOWN",
                    FailureKind.Validation,
                    $"Asset role '{role}' is not declared by the Logic Module.");
            }

            if (command.AssetId == Guid.Empty)
            {
                throw new WellSiteAutoPilotException(
                    FailureCodes.ValidationFailed,
                    FailureKind.Validation,
                    $"Asset binding for role '{role}' requires an Asset ID.");
            }

            if (!uniqueBindings.Add($"{role}:{command.AssetId:D}"))
            {
                throw new WellSiteAutoPilotException(
                    "CONFIGURED_LOGIC_ASSET_BINDING_DUPLICATE",
                    FailureKind.Validation,
                    $"Asset '{command.AssetId}' is bound more than once to role '{role}'.");
            }

            ValidateJson(
                command.ParameterOverridesJson,
                $"Parameter overrides for role '{role}' must be valid JSON.");

            var asset = await assetRepository.GetAssetAsync(command.AssetId, cancellationToken);
            if (asset is null || !asset.IsActive)
            {
                throw new WellSiteAutoPilotException(
                    "CONFIGURED_LOGIC_ASSET_NOT_FOUND",
                    FailureKind.NotFound,
                    $"Asset '{command.AssetId}' was not found or is inactive.");
            }

            if (!string.IsNullOrWhiteSpace(requirement.RequiredAssetTypeKey))
            {
                var assetType = await assetRepository.GetAssetTypeAsync(
                    asset.AssetTypeId,
                    cancellationToken);

                if (assetType is null ||
                    !assetType.IsActive ||
                    !string.Equals(
                        assetType.Key,
                        requirement.RequiredAssetTypeKey,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new WellSiteAutoPilotException(
                        "CONFIGURED_LOGIC_ASSET_TYPE_MISMATCH",
                        FailureKind.Validation,
                        $"Asset '{command.AssetId}' does not satisfy role '{role}' type requirement '{requirement.RequiredAssetTypeKey}'.");
                }
            }

            normalized.Add(new ConfiguredLogicAssetBinding(
                role,
                command.AssetId,
                NormalizeJson(command.ParameterOverridesJson)));
        }

        foreach (var requirement in manifest.AssetRequirements)
        {
            var count = normalized.Count(
                item => string.Equals(
                    item.Role,
                    requirement.Role,
                    StringComparison.OrdinalIgnoreCase));

            if (count < requirement.MinimumCount || count > requirement.MaximumCount)
            {
                throw new WellSiteAutoPilotException(
                    "CONFIGURED_LOGIC_ASSET_CARDINALITY_INVALID",
                    FailureKind.Validation,
                    $"Asset role '{requirement.Role}' requires between {requirement.MinimumCount} and {requirement.MaximumCount} bindings; {count} were provided.");
            }
        }

        return normalized;
    }

    private static IReadOnlyCollection<ConfiguredLogicDataBinding> ValidateDataBindings(
        LogicModuleManifest manifest,
        IReadOnlyCollection<ConfiguredLogicAssetBinding> assetBindings,
        IReadOnlyCollection<ConfiguredLogicDataBindingCommand> commands)
    {
        if (commands is null)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Configured Logic data bindings are required.");
        }

        var requirements = manifest.DataRequirements
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var normalized = new List<ConfiguredLogicDataBinding>(commands.Count);
        var uniqueBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in commands)
        {
            var requirementId = RequireText(
                command.RequirementId,
                "Configured Logic data requirement ID is required.");

            if (!requirements.TryGetValue(requirementId, out var requirement))
            {
                throw new WellSiteAutoPilotException(
                    "CONFIGURED_LOGIC_DATA_REQUIREMENT_UNKNOWN",
                    FailureKind.Validation,
                    $"Data requirement '{requirementId}' is not declared by the Logic Module.");
            }

            if (command.AssetId == Guid.Empty)
            {
                throw new WellSiteAutoPilotException(
                    FailureCodes.ValidationFailed,
                    FailureKind.Validation,
                    $"Data requirement '{requirementId}' requires an Asset ID.");
            }

            var roleBindingExists = assetBindings.Any(
                item => item.AssetId == command.AssetId &&
                        string.Equals(
                            item.Role,
                            requirement.AssetRole,
                            StringComparison.OrdinalIgnoreCase));

            if (!roleBindingExists)
            {
                throw new WellSiteAutoPilotException(
                    "CONFIGURED_LOGIC_DATA_ASSET_ROLE_MISMATCH",
                    FailureKind.Validation,
                    $"Data requirement '{requirementId}' must bind to an Asset in role '{requirement.AssetRole}'.");
            }

            var providerId = RequireText(
                command.ProviderId,
                $"Provider ID for data requirement '{requirementId}' is required.");
            var providerAssetExternalId = RequireText(
                command.ProviderAssetExternalId,
                $"Provider Asset ID for data requirement '{requirementId}' is required.");

            ValidateJson(
                command.ProviderMappingJson,
                $"Provider mapping for data requirement '{requirementId}' must be valid JSON.");

            if (!uniqueBindings.Add($"{requirementId}:{command.AssetId:D}"))
            {
                throw new WellSiteAutoPilotException(
                    "CONFIGURED_LOGIC_DATA_BINDING_DUPLICATE",
                    FailureKind.Validation,
                    $"Data requirement '{requirementId}' is bound more than once for Asset '{command.AssetId}'.");
            }

            normalized.Add(new ConfiguredLogicDataBinding(
                requirementId,
                command.AssetId,
                providerId,
                providerAssetExternalId,
                NormalizeJson(command.ProviderMappingJson)));
        }

        foreach (var requirement in manifest.DataRequirements)
        {
            var requiredAssets = assetBindings
                .Where(item => string.Equals(
                    item.Role,
                    requirement.AssetRole,
                    StringComparison.OrdinalIgnoreCase))
                .Select(item => item.AssetId)
                .ToArray();

            foreach (var assetId in requiredAssets)
            {
                if (!normalized.Any(
                        item => item.AssetId == assetId &&
                                string.Equals(
                                    item.RequirementId,
                                    requirement.Id,
                                    StringComparison.OrdinalIgnoreCase)))
                {
                    throw new WellSiteAutoPilotException(
                        "CONFIGURED_LOGIC_DATA_BINDING_MISSING",
                        FailureKind.Validation,
                        $"Data requirement '{requirement.Id}' has no provider binding for Asset '{assetId}'.");
                }
            }
        }

        return normalized;
    }

    private static ConfiguredLogicSchedule? ValidateSchedule(
        LogicModuleManifest manifest,
        ConfiguredLogicScheduleCommand? command)
    {
        if (command is null)
        {
            return null;
        }

        if (manifest.ExecutionProfile != ExecutionProfile.ScheduledOneShot)
        {
            throw new WellSiteAutoPilotException(
                "CONFIGURED_LOGIC_SCHEDULE_PROFILE_INVALID",
                FailureKind.Validation,
                "A cadence schedule can only be assigned to a ScheduledOneShot Logic Module.");
        }

        if (command.CadenceSeconds <= 0)
        {
            throw new WellSiteAutoPilotException(
                "CONFIGURED_LOGIC_SCHEDULE_CADENCE_INVALID",
                FailureKind.Validation,
                "Schedule cadence must be greater than zero seconds.");
        }

        var timeZoneId = RequireText(
            command.TimeZoneId,
            "Schedule timezone is required.");

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new WellSiteAutoPilotException(
                "CONFIGURED_LOGIC_SCHEDULE_TIMEZONE_INVALID",
                FailureKind.Validation,
                $"Schedule timezone '{timeZoneId}' is not recognized.",
                innerException: exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new WellSiteAutoPilotException(
                "CONFIGURED_LOGIC_SCHEDULE_TIMEZONE_INVALID",
                FailureKind.Validation,
                $"Schedule timezone '{timeZoneId}' is invalid.",
                innerException: exception);
        }

        return new ConfiguredLogicSchedule(
            command.Enabled,
            command.CadenceSeconds,
            command.StartAtUtc.ToUniversalTime(),
            timeZoneId);
    }

    private static ConfiguredLogicRevision GetRequiredRevision(
        ConfiguredLogicDefinition configuredLogic,
        Guid revisionId) =>
        configuredLogic.Revisions.SingleOrDefault(item => item.Id == revisionId) ??
        throw new WellSiteAutoPilotException(
            "CONFIGURED_LOGIC_REVISION_NOT_FOUND",
            FailureKind.NotFound,
            "The requested Configured Logic revision was not found.");

    internal static LogicModuleManifest DeserializeAndValidateManifest(string json)
    {
        ValidateJson(json, "Logic Module manifest must be valid JSON.");

        LogicModuleManifest manifest;

        try
        {
            manifest = JsonSerializer.Deserialize<LogicModuleManifest>(
                json,
                ManifestSerializerOptions) ??
                throw new JsonException("Logic Module manifest was empty.");
        }
        catch (JsonException exception)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Logic Module manifest could not be deserialized.",
                innerException: exception);
        }

        LogicModuleManifestValidator.Validate(manifest);
        return manifest;
    }

    private static string RequireText(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                message);
        }

        return value.Trim();
    }

    private static void ValidateJson(string json, string message)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                message);
        }

        try
        {
            using var _ = JsonDocument.Parse(json);
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

    private static string NormalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static JsonSerializerOptions CreateManifestSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
