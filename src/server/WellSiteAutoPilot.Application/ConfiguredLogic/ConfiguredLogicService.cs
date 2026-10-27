using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Assets.Views;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.ConfiguredLogic;

public sealed class ConfiguredLogicService(
    IConfiguredLogicRepository repository,
    IAssetRepository assetRepository,
    ILogicModuleCatalogRepository moduleCatalogRepository,
    TimeProvider timeProvider,
    IAssetViewRepository? assetViews = null,
    IConnectionRepository? connections = null)
{
    private static readonly JsonSerializerOptions ManifestSerializerOptions = CreateManifestSerializerOptions();

    public async Task<ConfiguredLogicDefinition> CreateAsync(
        CreateConfiguredLogicCommand command,
        SecurityActorContext? actor = null,
        CancellationToken cancellationToken = default)
    {
        var definition = await BuildDefinitionAsync(command, cancellationToken);
        await repository.AddAsync(definition, actor, cancellationToken);
        return definition;
    }

    public async Task<ConfiguredLogicDefinition> CreateAuthorizedAsync(
        CreateConfiguredLogicCommand command, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var definition = await BuildDefinitionAsync(command, cancellationToken, subject);
        await repository.AddAuthorizedAsync(definition, subject, actor, cancellationToken);
        return definition;
    }

    private async Task<ConfiguredLogicDefinition> BuildDefinitionAsync(
        CreateConfiguredLogicCommand command, CancellationToken cancellationToken, AccessSubject? subject = null)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = RequireText(command.Name, "Configured Logic name is required.");
        if (name.Length > 256)
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed,
                FailureKind.Validation, "Configured Logic name must not exceed 256 characters.");
        var requestedManifest = DeserializeAndValidateManifest(command.ModuleManifestJson);
        var installedModule = await GetInstalledModuleAsync(
            requestedManifest.ModuleId,
            requestedManifest.Version,
            cancellationToken);
        EnsureManifestMatchesInstalled(command.ModuleManifestJson, installedModule);
        var manifest = DeserializeAndValidateManifest(installedModule.ManifestJson);
        ValidateParameterObject(command.ParametersJson);
        await LogicModuleManifestValidator.ValidateParametersAsync(
            manifest, command.ParametersJson, cancellationToken);

        var (targetBindings, viewBinding) = await ResolveViewAsync(command.View, command.AssetBindings, manifest, subject, cancellationToken);
        var assetBindings = await ValidateAssetBindingsAsync(
            manifest,
            targetBindings,
            cancellationToken,
            viewBinding?.ConnectionId);
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
            dataBindings,
            viewBinding);

        var configuredLogic = new ConfiguredLogicDefinition(
            configuredLogicId,
            name,
            null,
            now,
            [revision]);

        return configuredLogic;
    }

    public async Task<ConfiguredLogicRevision> CreateRevisionAsync(
        Guid configuredLogicId,
        CreateConfiguredLogicRevisionCommand command,
        SecurityActorContext? actor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var existing = await GetRequiredAsync(configuredLogicId, cancellationToken);
        var revision = await BuildRevisionAsync(existing, command, cancellationToken);
        await repository.AddRevisionAsync(revision, actor, cancellationToken);
        return revision;
    }

    public async Task<ConfiguredLogicRevision> CreateRevisionAuthorizedAsync(
        Guid configuredLogicId, CreateConfiguredLogicRevisionCommand command, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        var existing = await repository.GetAuthorizedAsync(subject, configuredLogicId, cancellationToken) ??
            throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_NOT_FOUND", FailureKind.NotFound,
                "The requested Configured Logic was not found.");
        var revision = await BuildRevisionAsync(existing, command, cancellationToken, subject);
        await repository.AddRevisionAuthorizedAsync(revision, subject, actor, cancellationToken);
        return revision;
    }

    private async Task<ConfiguredLogicRevision> BuildRevisionAsync(ConfiguredLogicDefinition existing,
        CreateConfiguredLogicRevisionCommand command, CancellationToken cancellationToken, AccessSubject? subject = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        var requestedManifest = DeserializeAndValidateManifest(command.ModuleManifestJson);
        var installedModule = await GetInstalledModuleAsync(
            requestedManifest.ModuleId,
            requestedManifest.Version,
            cancellationToken);
        EnsureManifestMatchesInstalled(command.ModuleManifestJson, installedModule);
        var manifest = DeserializeAndValidateManifest(installedModule.ManifestJson);
        ValidateParameterObject(command.ParametersJson);
        await LogicModuleManifestValidator.ValidateParametersAsync(
            manifest, command.ParametersJson, cancellationToken);

        var (targetBindings, viewBinding) = await ResolveViewAsync(command.View, command.AssetBindings, manifest, subject, cancellationToken);
        var assetBindings = await ValidateAssetBindingsAsync(
            manifest,
            targetBindings,
            cancellationToken,
            viewBinding?.ConnectionId);
        var dataBindings = ValidateDataBindings(
            manifest,
            assetBindings,
            command.DataBindings);
        var schedule = ValidateSchedule(manifest, command.Schedule);

        var revision = new ConfiguredLogicRevision(
            Guid.NewGuid(),
            existing.Id,
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
            dataBindings,
            viewBinding);

        return revision;
    }

    public Task<ConfiguredLogicRevision> ValidateRevisionAsync(
        Guid configuredLogicId,
        Guid revisionId,
        SecurityActorContext? actor = null,
        CancellationToken cancellationToken = default) =>
        ValidateRevisionCoreAsync(configuredLogicId, revisionId, actor, null, cancellationToken);

    public Task<ConfiguredLogicRevision> ValidateRevisionAuthorizedAsync(Guid configuredLogicId, Guid revisionId,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        return ValidateRevisionCoreAsync(configuredLogicId, revisionId, actor, subject, cancellationToken);
    }

    private async Task<ConfiguredLogicRevision> ValidateRevisionCoreAsync(Guid configuredLogicId, Guid revisionId,
        SecurityActorContext? actor, AccessSubject? subject, CancellationToken cancellationToken)
    {
        var configuredLogic = await ReadForLifecycleAsync(configuredLogicId, subject, cancellationToken);
        var revision = GetRequiredRevision(configuredLogic, revisionId);

        if (revision.Status != ConfiguredLogicRevisionStatus.Draft)
        {
            throw new WellSiteAutoPilotException(
                "CONFIGURED_LOGIC_REVISION_NOT_DRAFT",
                FailureKind.Conflict,
                "Only Draft Configured Logic revisions can be validated.");
        }

        var installedModule = await GetInstalledModuleAsync(
            revision.ModuleId,
            revision.ModuleVersion,
            cancellationToken);
        EnsureModuleEnabled(installedModule);
        EnsureManifestMatchesInstalled(revision.ModuleManifestJson, installedModule);
        ValidateParameterObject(revision.ParametersJson);
        var manifest = DeserializeAndValidateManifest(installedModule.ManifestJson);
        await LogicModuleManifestValidator.ValidateParametersAsync(
            manifest, revision.ParametersJson, cancellationToken);
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

        if (subject is not null)
            await repository.SetRevisionValidatedAuthorizedAsync(revision, subject, actor!, cancellationToken);
        else
            await repository.SetRevisionValidatedAsync(configuredLogicId, revisionId, timeProvider.GetUtcNow(), actor, cancellationToken);

        var updated = await ReadForLifecycleAsync(configuredLogicId, subject, cancellationToken);
        return GetRequiredRevision(updated, revisionId);
    }

    public Task<ConfiguredLogicRevision> ActivateRevisionAsync(
        Guid configuredLogicId,
        Guid revisionId,
        SecurityActorContext? actor = null,
        string? expectedActivationToken = null,
        CancellationToken cancellationToken = default) =>
        ActivateRevisionCoreAsync(configuredLogicId, revisionId, actor, expectedActivationToken, null, cancellationToken);

    public Task<ConfiguredLogicRevision> ActivateRevisionAuthorizedAsync(Guid configuredLogicId, Guid revisionId,
        string expectedActivationToken, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(expectedActivationToken))
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "The reviewed active revision token is required.");
        return ActivateRevisionCoreAsync(configuredLogicId, revisionId, actor, expectedActivationToken, subject, cancellationToken);
    }

    private async Task<ConfiguredLogicRevision> ActivateRevisionCoreAsync(Guid configuredLogicId, Guid revisionId,
        SecurityActorContext? actor, string? expectedActivationToken, AccessSubject? subject, CancellationToken cancellationToken)
    {
        var configuredLogic = await ReadForLifecycleAsync(configuredLogicId, subject, cancellationToken);
        var revision = GetRequiredRevision(configuredLogic, revisionId);

        if (revision.Status != ConfiguredLogicRevisionStatus.Validated)
        {
            throw new WellSiteAutoPilotException(
                "CONFIGURED_LOGIC_REVISION_NOT_VALIDATED",
                FailureKind.Conflict,
                "Only Validated Configured Logic revisions can be activated.");
        }

        var installedModule = await GetInstalledModuleAsync(
            revision.ModuleId,
            revision.ModuleVersion,
            cancellationToken);
        EnsureModuleEnabled(installedModule);
        EnsureManifestMatchesInstalled(revision.ModuleManifestJson, installedModule);

        // A browser must activate against the active revision it actually reviewed.
        var token = configuredLogic.ActiveRevisionId?.ToString("D") ?? "none";
        if (expectedActivationToken is not null &&
            !string.Equals(expectedActivationToken, token, StringComparison.OrdinalIgnoreCase))
        {
            throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_REVISION_CONFLICT",
                FailureKind.Conflict, "The active revision changed. Refresh and review before activating.");
        }

        // Assets may have been retired after validation. Check again at activation.
        var manifest = DeserializeAndValidateManifest(installedModule.ManifestJson);
        await ValidateAssetBindingsAsync(manifest, revision.AssetBindings.Select(item =>
            new ConfiguredLogicAssetBindingCommand(item.Role, item.AssetId,
                item.ParameterOverridesJson)).ToArray(), cancellationToken);

        if (subject is not null)
            await repository.ActivateRevisionAuthorizedAsync(revision, configuredLogic.ActiveRevisionId, subject, actor!, cancellationToken);
        else
            await repository.ActivateRevisionAsync(configuredLogicId, revisionId, timeProvider.GetUtcNow(),
                configuredLogic.ActiveRevisionId, actor, cancellationToken);

        var updated = await ReadForLifecycleAsync(configuredLogicId, subject, cancellationToken);
        return GetRequiredRevision(updated, revisionId);
    }

    private async Task<ConfiguredLogicDefinition> ReadForLifecycleAsync(Guid id, AccessSubject? subject,
        CancellationToken cancellationToken) => subject is null
        ? await GetRequiredAsync(id, cancellationToken)
        : await repository.GetAuthorizedAsync(subject, id, cancellationToken) ??
            throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_NOT_FOUND", FailureKind.NotFound,
                "The requested Configured Logic was not found.");

    public async Task<ConfiguredShadowExecutionCommand> PrepareManualShadowAsync(
        Guid configuredLogicId, Guid revisionId, CancellationToken cancellationToken = default)
    {
        var definition = await GetRequiredAsync(configuredLogicId, cancellationToken);
        var revision = GetRequiredRevision(definition, revisionId);
        if (definition.ActiveRevisionId != revisionId ||
            revision.Status != ConfiguredLogicRevisionStatus.Active || revision.Mode != ExecutionMode.Shadow)
            throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_REVISION_CONFLICT",
                FailureKind.Conflict, "Review the current active Shadow revision before running.");
        var installed = await GetInstalledModuleAsync(revision.ModuleId, revision.ModuleVersion, cancellationToken);
        EnsureModuleEnabled(installed);
        EnsureManifestMatchesInstalled(revision.ModuleManifestJson, installed);
        var manifest = DeserializeAndValidateManifest(installed.ManifestJson);
        await ValidateAssetBindingsAsync(manifest, revision.AssetBindings.Select(b =>
            new ConfiguredLogicAssetBindingCommand(b.Role, b.AssetId, b.ParameterOverridesJson)).ToArray(), cancellationToken);
        return ConfiguredExecutionSnapshot.Create(definition, revision, ExecutionTriggerKind.Manual, null);
    }

    public async Task<ConfiguredLogicDefinition> GetRequiredAsync(
        Guid configuredLogicId,
        CancellationToken cancellationToken = default) =>
        await repository.GetAsync(configuredLogicId, cancellationToken) ??
        throw new WellSiteAutoPilotException(
            "CONFIGURED_LOGIC_NOT_FOUND",
            FailureKind.NotFound,
            "The requested Configured Logic was not found.");

    public async Task<ConfiguredLogicDefinition> GetRequiredInScopeAsync(
        Guid configuredLogicId,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedAssetIds);

        return await repository.GetInScopeAsync(
                   configuredLogicId,
                   allowedAssetIds,
                   cancellationToken) ??
               throw new WellSiteAutoPilotException(
                   "CONFIGURED_LOGIC_NOT_FOUND",
                   FailureKind.NotFound,
                   "The requested Configured Logic was not found.");
    }

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

    public Task<IReadOnlyCollection<ConfiguredLogicDefinition>> ListInScopeAsync(
        int limit,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedAssetIds);

        if (limit is < 1 or > 500)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Configured Logic list limit must be between 1 and 500.");
        }

        return repository.ListInScopeAsync(
            limit,
            allowedAssetIds,
            cancellationToken);
    }

    private async Task<(IReadOnlyCollection<ConfiguredLogicAssetBindingCommand> Bindings, ConfiguredLogicViewBinding? View)> ResolveViewAsync(
        ConfiguredLogicViewBindingCommand? requested, IReadOnlyCollection<ConfiguredLogicAssetBindingCommand> bindings,
        LogicModuleManifest manifest, AccessSubject? subject, CancellationToken cancellationToken)
    {
        if (bindings is null)
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "Configured Logic Asset bindings are required.");
        if (requested is null)
        {
            if (manifest.RequiredAdapterKey is not null &&
                (subject is null || !string.Equals(manifest.RequiredAdapterKey, "foresite", StringComparison.OrdinalIgnoreCase) ||
                 bindings.Select(binding => binding.AssetId).Distinct().Count() != 1))
                throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                    "Select one authorized ForeSite well directly, or use a compatible Connection View.");
            return (bindings, null);
        }
        if (subject is null || assetViews is null || connections is null || requested.ViewId == Guid.Empty || requested.ExpectedRevision < 1)
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation, "A reviewed View selection is required.");
        var role = RequireText(requested.Role, "A View target role is required.", 128);
        if (!manifest.AssetRequirements.Any(item => string.Equals(item.Role, role, StringComparison.OrdinalIgnoreCase)))
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation, "The module does not declare that target role.");
        var view = await assetViews.FindAsync(subject, requested.ViewId, cancellationToken) ??
            throw new WellSiteAutoPilotException("ASSET_VIEW_NOT_FOUND", FailureKind.NotFound, "The View was not found.");
        var selection = await assetViews.SelectionAsync(subject, requested.ViewId, cancellationToken);
        if (selection.UnavailableMembers.Count > 0)
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "Remove inactive or ineligible wells from the View before assigning it.");
        if (selection.Revision != requested.ExpectedRevision)
            throw new WellSiteAutoPilotException("ASSET_VIEW_REVISION_STALE", FailureKind.Conflict, "The View changed. Review its selection again.");
        var connection = await connections.FindAsync(view.ConnectionId, cancellationToken);
        if (connection is null || !connection.IsEnabled || (manifest.RequiredAdapterKey is { } adapter &&
            !string.Equals(adapter, connection.AdapterKey, StringComparison.OrdinalIgnoreCase)))
            throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_VIEW_INCOMPATIBLE", FailureKind.Validation,
                "The View's Connection is disabled or incompatible with this module.");
        var supplied = bindings.Where(item => string.Equals(item.Role, role, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (supplied.Length > 0 && (supplied.Select(item => item.AssetId).Distinct().Count() != supplied.Length ||
            !selection.AssetIds.ToHashSet().SetEquals(supplied.Select(item => item.AssetId))))
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "The supplied targets do not match the complete View selection.");
        var overrides = supplied.ToDictionary(item => item.AssetId, item => item.ParameterOverridesJson);
        return (bindings.Where(item => !string.Equals(item.Role, role, StringComparison.OrdinalIgnoreCase))
                .Concat(selection.AssetIds.Select(id => new ConfiguredLogicAssetBindingCommand(role, id, overrides.GetValueOrDefault(id) ?? "{}"))).ToArray(),
            new ConfiguredLogicViewBinding(view.Id, selection.Revision, view.ConnectionId, role));
    }

    private async Task<IReadOnlyCollection<ConfiguredLogicAssetBinding>> ValidateAssetBindingsAsync(
        LogicModuleManifest manifest,
        IReadOnlyCollection<ConfiguredLogicAssetBindingCommand> commands,
        CancellationToken cancellationToken, Guid? requiredConnectionId = null)
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

        var assetsById = (await assetRepository.GetAssetsByIdsAsync(commands.Select(command => command.AssetId).ToArray(), cancellationToken))
            .ToDictionary(asset => asset.Id);
        var typesById = (await assetRepository.ListAssetTypesAsync(cancellationToken)).ToDictionary(type => type.Id);
        var normalized = new List<ConfiguredLogicAssetBinding>(commands.Count);
        var uniqueBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in commands)
        {
            var role = RequireText(command.Role, "Configured Logic asset role is required (up to 128 characters).", 128);

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

            ValidateParameterObject(command.ParameterOverridesJson);

            var asset = assetsById.GetValueOrDefault(command.AssetId);
            if (asset is null || !asset.IsActive || (requiredConnectionId is { } owner && asset.OwnerConnectionId != owner))
            {
                throw new WellSiteAutoPilotException(
                    "CONFIGURED_LOGIC_ASSET_NOT_FOUND",
                    FailureKind.NotFound,
                    $"Asset '{command.AssetId}' was not found or is inactive.");
            }

            if (!string.IsNullOrWhiteSpace(requirement.RequiredAssetTypeKey))
            {
                var assetType = typesById.GetValueOrDefault(asset.AssetTypeId);

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

    private static List<ConfiguredLogicDataBinding> ValidateDataBindings(
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
        var boundRoles = assetBindings.Select(binding => (binding.Role.ToUpperInvariant(), binding.AssetId)).ToHashSet();
        var normalized = new List<ConfiguredLogicDataBinding>(commands.Count);
        var uniqueBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var command in commands)
        {
            var requirementId = RequireText(
                command.RequirementId,
                "Configured Logic data requirement ID is required (up to 128 characters).", 128);

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

            var roleBindingExists = boundRoles.Contains((requirement.AssetRole.ToUpperInvariant(), command.AssetId));

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
                $"Provider Asset ID for data requirement '{requirementId}' is required (up to 512 characters).", 512);

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
                if (!uniqueBindings.Contains($"{requirement.Id}:{assetId:D}"))
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


    private async Task<InstalledLogicModule> GetInstalledModuleAsync(
        string moduleId,
        string version,
        CancellationToken cancellationToken) =>
        await moduleCatalogRepository.GetAsync(
            moduleId,
            version,
            cancellationToken) ??
        throw new WellSiteAutoPilotException(
            "CONFIGURED_LOGIC_MODULE_NOT_INSTALLED",
            FailureKind.DependencyUnavailable,
            $"Logic Module '{moduleId}' version '{version}' is not installed.");

    private static void EnsureModuleEnabled(InstalledLogicModule installedModule)
    {
        if (!installedModule.IsEnabled)
        {
            throw new WellSiteAutoPilotException(
                "CONFIGURED_LOGIC_MODULE_NOT_TRUSTED",
                FailureKind.DependencyUnavailable,
                $"Logic Module '{installedModule.ModuleId}' version '{installedModule.Version}' is not trusted and enabled.");
        }
    }

    private static void EnsureManifestMatchesInstalled(
        string requestedManifestJson,
        InstalledLogicModule installedModule)
    {
        if (!string.Equals(
                NormalizeJson(requestedManifestJson),
                NormalizeJson(installedModule.ManifestJson),
                StringComparison.Ordinal))
        {
            throw new WellSiteAutoPilotException(
                "CONFIGURED_LOGIC_MODULE_MANIFEST_MISMATCH",
                FailureKind.Conflict,
                $"Configured Logic manifest does not match installed Logic Module '{installedModule.ModuleId}' version '{installedModule.Version}'.");
        }
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

    private static string RequireText(string value, string message, int maximumLength = 256)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximumLength)
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
        if (string.IsNullOrWhiteSpace(json) || json.Length > 65536 ||
            global::System.Text.Encoding.UTF8.GetByteCount(json) > 65536)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                message);
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (HasDuplicateProperties(document.RootElement))
                throw new JsonException("JSON is oversized or contains duplicate property names.");
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

    private static void ValidateParameterObject(string json)
    {
        ValidateJson(json, "Parameters and overrides require a bounded JSON object with unique property names.");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed,
                FailureKind.Validation, "Parameters and overrides must be JSON objects.");
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || HasDuplicateProperties(property.Value)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                if (HasDuplicateProperties(item)) return true;
        return false;
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
