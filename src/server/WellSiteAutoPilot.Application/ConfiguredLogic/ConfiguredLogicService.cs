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

        var bindings = await ValidateBindingsAsync(
            manifest,
            command.AssetBindings,
            cancellationToken);

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
            bindings);

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

        var bindings = await ValidateBindingsAsync(
            manifest,
            command.AssetBindings,
            cancellationToken);

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
            bindings);

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
        await ValidateBindingsAsync(
            manifest,
            revision.AssetBindings
                .Select(item => new ConfiguredLogicAssetBindingCommand(
                    item.Role,
                    item.AssetId,
                    item.ParameterOverridesJson))
                .ToArray(),
            cancellationToken);

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

    private async Task<IReadOnlyCollection<ConfiguredLogicAssetBinding>> ValidateBindingsAsync(
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

    private static ConfiguredLogicRevision GetRequiredRevision(
        ConfiguredLogicDefinition configuredLogic,
        Guid revisionId) =>
        configuredLogic.Revisions.SingleOrDefault(item => item.Id == revisionId) ??
        throw new WellSiteAutoPilotException(
            "CONFIGURED_LOGIC_REVISION_NOT_FOUND",
            FailureKind.NotFound,
            "The requested Configured Logic revision was not found.");

    private static LogicModuleManifest DeserializeAndValidateManifest(string json)
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
