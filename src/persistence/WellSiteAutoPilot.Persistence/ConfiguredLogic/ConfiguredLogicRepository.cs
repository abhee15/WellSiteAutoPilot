using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Application.Security;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;

namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed partial class ConfiguredLogicRepository(
    WellSiteAutoPilotDbContext dbContext, TimeProvider? clock = null) : IConfiguredLogicRepository
{
    public async Task AddAsync(
        ConfiguredLogicDefinition configuredLogic,
        SecurityActorContext? actor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuredLogic);

        var initialRevision = configuredLogic.Revisions.Single();

        dbContext.ConfiguredLogicDefinitions.Add(new ConfiguredLogicEntity
        {
            Id = configuredLogic.Id,
            Name = configuredLogic.Name,
            ActiveRevisionId = configuredLogic.ActiveRevisionId,
            CreatedAtUtc = configuredLogic.CreatedAtUtc
        });

        AddRevision(initialRevision);
        Audit(initialRevision, "CONFIGURED_LOGIC_CREATED", actor, initialRevision.CreatedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRevisionAsync(
        ConfiguredLogicRevision revision,
        SecurityActorContext? actor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockDefinitionAsync(revision.ConfiguredLogicId, cancellationToken);
        var latest = await dbContext.ConfiguredLogicRevisions.AsNoTracking()
            .Where(item => item.ConfiguredLogicId == revision.ConfiguredLogicId)
            .MaxAsync(item => (int?)item.RevisionNumber, cancellationToken) ?? 0;
        if (revision.RevisionNumber != latest + 1) throw Conflict();
        AddRevision(revision);
        Audit(revision, "CONFIGURED_LOGIC_REVISION_CREATED", actor, revision.CreatedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ConfiguredLogicDefinition?> GetAsync(
        Guid configuredLogicId,
        CancellationToken cancellationToken = default)
    {
        var definition = await dbContext.ConfiguredLogicDefinitions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == configuredLogicId, cancellationToken);

        if (definition is null)
        {
            return null;
        }

        var revisions = await LoadRevisionsAsync(configuredLogicId, cancellationToken);
        return ToDomain(definition, revisions);
    }

    public async Task<ConfiguredLogicDefinition?> GetInScopeAsync(
        Guid configuredLogicId,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedAssetIds);

        if (allowedAssetIds.Count == 0)
        {
            return null;
        }

        var ids = allowedAssetIds.Distinct().ToArray();
        var definition = await dbContext.ConfiguredLogicDefinitions
            .AsNoTracking()
            .Where(item => item.Id == configuredLogicId)
            .Where(item =>
                dbContext.ConfiguredLogicRevisions.Any(
                    revision =>
                        revision.ConfiguredLogicId == item.Id &&
                        dbContext.ConfiguredLogicAssetBindings.Any(
                            binding =>
                                binding.RevisionId == revision.Id)) &&
                !dbContext.ConfiguredLogicRevisions.Any(
                    revision =>
                        revision.ConfiguredLogicId == item.Id &&
                        dbContext.ConfiguredLogicAssetBindings.Any(
                            binding =>
                                binding.RevisionId == revision.Id &&
                                !ids.Contains(binding.AssetId))))
            .SingleOrDefaultAsync(cancellationToken);

        if (definition is null)
        {
            return null;
        }

        var revisions = await LoadRevisionsAsync(
            configuredLogicId,
            cancellationToken);

        return ToDomain(definition, revisions);
    }

    public async Task<IReadOnlyCollection<ConfiguredLogicDefinition>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var definitions = await dbContext.ConfiguredLogicDefinitions
            .AsNoTracking()
            .OrderBy(item => item.Name)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        var results = new List<ConfiguredLogicDefinition>(definitions.Length);

        foreach (var definition in definitions)
        {
            var revisions = await LoadRevisionsAsync(definition.Id, cancellationToken);
            results.Add(ToDomain(definition, revisions));
        }

        return results;
    }

    public async Task<IReadOnlyCollection<ConfiguredLogicDefinition>> ListInScopeAsync(
        int limit,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedAssetIds);

        if (allowedAssetIds.Count == 0)
        {
            return [];
        }

        var ids = allowedAssetIds.Distinct().ToArray();

        var definitions = await dbContext.ConfiguredLogicDefinitions
            .AsNoTracking()
            .Where(definition =>
                dbContext.ConfiguredLogicRevisions.Any(
                    revision =>
                        revision.ConfiguredLogicId == definition.Id &&
                        dbContext.ConfiguredLogicAssetBindings.Any(
                            binding =>
                                binding.RevisionId == revision.Id)) &&
                !dbContext.ConfiguredLogicRevisions.Any(
                    revision =>
                        revision.ConfiguredLogicId == definition.Id &&
                        dbContext.ConfiguredLogicAssetBindings.Any(
                            binding =>
                                binding.RevisionId == revision.Id &&
                                !ids.Contains(binding.AssetId))))
            .OrderBy(item => item.Name)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        var results = new List<ConfiguredLogicDefinition>(definitions.Length);

        foreach (var definition in definitions)
        {
            var revisions = await LoadRevisionsAsync(
                definition.Id,
                cancellationToken);
            results.Add(ToDomain(definition, revisions));
        }

        return results;
    }

    public async Task<IReadOnlyCollection<ConfiguredLogicDefinition>> ListActiveScheduledAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var rows = await (
            from definition in dbContext.ConfiguredLogicDefinitions.AsNoTracking()
            join revision in dbContext.ConfiguredLogicRevisions.AsNoTracking()
                on definition.ActiveRevisionId equals (Guid?)revision.Id
            where definition.ActiveRevisionId != null &&
                  revision.Status == nameof(ConfiguredLogicRevisionStatus.Active) &&
                  // Same set as ExecutionModePolicy; spelled out because the query is translated to SQL.
                  (revision.Mode == nameof(ExecutionMode.Shadow) || revision.Mode == nameof(ExecutionMode.Recommendation)) &&
                  revision.ScheduleJson != null &&
                  (revision.ViewConnectionId == null || dbContext.Connections.Any(connection =>
                      connection.Id == revision.ViewConnectionId && connection.IsEnabled))
            orderby definition.Name
            select new { Definition = definition, Revision = revision })
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        if (rows.Length == 0)
        {
            return [];
        }

        var revisionIds = rows
            .Select(item => item.Revision.Id)
            .ToArray();

        var assetBindings = await dbContext.ConfiguredLogicAssetBindings
            .AsNoTracking()
            .Where(item => revisionIds.Contains(item.RevisionId))
            .OrderBy(item => item.Role)
            .ThenBy(item => item.AssetId)
            .ToArrayAsync(cancellationToken);

        var dataBindings = await dbContext.ConfiguredLogicDataBindings
            .AsNoTracking()
            .Where(item => revisionIds.Contains(item.RevisionId))
            .OrderBy(item => item.RequirementId)
            .ThenBy(item => item.AssetId)
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(item =>
            {
                var revision = ToDomain(
                    item.Revision,
                    assetBindings
                        .Where(binding => binding.RevisionId == item.Revision.Id)
                        .Select(ToDomain)
                        .ToArray(),
                    dataBindings
                        .Where(binding => binding.RevisionId == item.Revision.Id)
                        .Select(ToDomain)
                        .ToArray());

                return ToDomain(
                    item.Definition,
                    [revision]);
            })
            .ToArray();
    }

    public async Task SetRevisionValidatedAsync(
        Guid configuredLogicId,
        Guid revisionId,
        DateTimeOffset validatedAtUtc,
        SecurityActorContext? actor = null,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockDefinitionAsync(configuredLogicId, cancellationToken);
        var changed = await dbContext.ConfiguredLogicRevisions
            .Where(item => item.Id == revisionId && item.ConfiguredLogicId == configuredLogicId &&
                item.Status == nameof(ConfiguredLogicRevisionStatus.Draft))
            .ExecuteUpdateAsync(set => set
                .SetProperty(item => item.Status, nameof(ConfiguredLogicRevisionStatus.Validated))
                .SetProperty(item => item.ValidatedAtUtc, validatedAtUtc), cancellationToken);
        if (changed != 1) throw Conflict();
        Audit(configuredLogicId, revisionId, "CONFIGURED_LOGIC_REVISION_VALIDATED", actor, validatedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ActivateRevisionAsync(
        Guid configuredLogicId,
        Guid revisionId,
        DateTimeOffset activatedAtUtc,
        Guid? expectedActiveRevisionId,
        SecurityActorContext? actor = null,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var definition = await LockDefinitionAsync(configuredLogicId, cancellationToken);
        if (definition.ActiveRevisionId != expectedActiveRevisionId) throw Conflict();
        var target = await dbContext.ConfiguredLogicRevisions.AsNoTracking().SingleAsync(
            item => item.Id == revisionId && item.ConfiguredLogicId == configuredLogicId, cancellationToken);
        if (target.Status != nameof(ConfiguredLogicRevisionStatus.Validated)) throw Conflict();

        await dbContext.ConfiguredLogicRevisions
            .Where(item => item.ConfiguredLogicId == configuredLogicId &&
                item.Status == nameof(ConfiguredLogicRevisionStatus.Active))
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.Status,
                nameof(ConfiguredLogicRevisionStatus.Superseded)), cancellationToken);
        await dbContext.ConfiguredLogicRevisions.Where(item => item.Id == revisionId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(item => item.Status, nameof(ConfiguredLogicRevisionStatus.Active))
                .SetProperty(item => item.ActivatedAtUtc, activatedAtUtc), cancellationToken);
        await dbContext.ConfiguredLogicDefinitions.Where(item => item.Id == configuredLogicId)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.ActiveRevisionId, revisionId), cancellationToken);
        Audit(configuredLogicId, revisionId, "CONFIGURED_LOGIC_REVISION_ACTIVATED", actor, activatedAtUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // One definition lock orders revision creation, validation and activation.
    // Use fresh, untracked rows: callers may reuse their scoped DbContext.
    private Task<ConfiguredLogicEntity> LockDefinitionAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ConfiguredLogicDefinitions.FromSqlInterpolated(
            $"SELECT * FROM logic.configured_logic WHERE \"Id\" = {id} FOR UPDATE")
            .AsNoTracking().SingleAsync(cancellationToken);

    private static WellSiteAutoPilotException Conflict() => new(
        "CONFIGURED_LOGIC_REVISION_CONFLICT", FailureKind.Conflict,
        "The configuration changed. Refresh and review before retrying.");

    private void Audit(ConfiguredLogicRevision revision, string action,
        SecurityActorContext? actor, DateTimeOffset now) =>
        Audit(revision.ConfiguredLogicId, revision.Id, action, actor, now);

    private void Audit(Guid id, Guid revisionId, string action,
        SecurityActorContext? actor, DateTimeOffset now)
    {
        // Service callers have an explicit system identity; HTTP supplies real user/correlation.
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(), OccurredAtUtc = now, ActorUserId = actor?.UserId,
            ActorIdentity = actor?.IdentityName ?? "system:configured-logic",
            Action = action, TargetType = "ConfiguredLogic", TargetId = id.ToString("D"),
            CorrelationId = actor?.CorrelationId,
            DetailsJson = JsonSerializer.Serialize(new { revisionId })
        });
    }

    private void AddRevision(ConfiguredLogicRevision revision)
    {
        dbContext.ConfiguredLogicRevisions.Add(new ConfiguredLogicRevisionEntity
        {
            Id = revision.Id,
            ViewId = revision.View?.ViewId, ViewRevision = revision.View?.Revision,
            ViewConnectionId = revision.View?.ConnectionId, ViewRole = revision.View?.Role,
            ConfiguredLogicId = revision.ConfiguredLogicId,
            RevisionNumber = revision.RevisionNumber,
            ModuleId = revision.ModuleId,
            ModuleVersion = revision.ModuleVersion,
            ModuleManifestJson = revision.ModuleManifestJson,
            Mode = revision.Mode.ToString(),
            ParametersJson = revision.ParametersJson,
            ScheduleJson = revision.Schedule is null ? null : JsonSerializer.Serialize(revision.Schedule),
            Status = revision.Status.ToString(),
            CreatedAtUtc = revision.CreatedAtUtc,
            ValidatedAtUtc = revision.ValidatedAtUtc,
            ActivatedAtUtc = revision.ActivatedAtUtc
        });

        foreach (var binding in revision.AssetBindings)
        {
            dbContext.ConfiguredLogicAssetBindings.Add(new ConfiguredLogicAssetBindingEntity
            {
                RevisionId = revision.Id,
                Role = binding.Role,
                AssetId = binding.AssetId,
                ParameterOverridesJson = binding.ParameterOverridesJson
            });
        }

        foreach (var binding in revision.DataBindings)
        {
            dbContext.ConfiguredLogicDataBindings.Add(new ConfiguredLogicDataBindingEntity
            {
                RevisionId = revision.Id,
                RequirementId = binding.RequirementId,
                AssetId = binding.AssetId,
                ProviderId = binding.ProviderId,
                ProviderAssetExternalId = binding.ProviderAssetExternalId,
                ProviderMappingJson = binding.ProviderMappingJson
            });
        }
    }

    private async Task<IReadOnlyCollection<ConfiguredLogicRevision>> LoadRevisionsAsync(
        Guid configuredLogicId,
        CancellationToken cancellationToken)
    {
        var revisions = await dbContext.ConfiguredLogicRevisions
            .AsNoTracking()
            .Where(item => item.ConfiguredLogicId == configuredLogicId)
            .OrderBy(item => item.RevisionNumber)
            .ToArrayAsync(cancellationToken);

        if (revisions.Length == 0)
        {
            return [];
        }

        var revisionIds = revisions.Select(item => item.Id).ToArray();
        var assetBindings = await dbContext.ConfiguredLogicAssetBindings
            .AsNoTracking()
            .Where(item => revisionIds.Contains(item.RevisionId))
            .OrderBy(item => item.Role)
            .ThenBy(item => item.AssetId)
            .ToArrayAsync(cancellationToken);

        var dataBindings = await dbContext.ConfiguredLogicDataBindings
            .AsNoTracking()
            .Where(item => revisionIds.Contains(item.RevisionId))
            .OrderBy(item => item.RequirementId)
            .ThenBy(item => item.AssetId)
            .ToArrayAsync(cancellationToken);

        return revisions
            .Select(revision => ToDomain(
                revision,
                assetBindings
                    .Where(binding => binding.RevisionId == revision.Id)
                    .Select(ToDomain)
                    .ToArray(),
                dataBindings
                    .Where(binding => binding.RevisionId == revision.Id)
                    .Select(ToDomain)
                    .ToArray()))
            .ToArray();
    }

    private static ConfiguredLogicDefinition ToDomain(
        ConfiguredLogicEntity entity,
        IReadOnlyCollection<ConfiguredLogicRevision> revisions) => new(
            entity.Id,
            entity.Name,
            entity.ActiveRevisionId,
            entity.CreatedAtUtc,
            revisions);

    private static ConfiguredLogicRevision ToDomain(
        ConfiguredLogicRevisionEntity entity,
        IReadOnlyCollection<ConfiguredLogicAssetBinding> assetBindings,
        IReadOnlyCollection<ConfiguredLogicDataBinding> dataBindings) => new(
            entity.Id,
            entity.ConfiguredLogicId,
            entity.RevisionNumber,
            entity.ModuleId,
            entity.ModuleVersion,
            entity.ModuleManifestJson,
            Enum.Parse<ExecutionMode>(entity.Mode),
            entity.ParametersJson,
            Enum.Parse<ConfiguredLogicRevisionStatus>(entity.Status),
            entity.CreatedAtUtc,
            entity.ValidatedAtUtc,
            entity.ActivatedAtUtc,
            entity.ScheduleJson is null
                ? null
                : JsonSerializer.Deserialize<ConfiguredLogicSchedule>(entity.ScheduleJson),
            assetBindings,
            dataBindings,
            entity.ViewId is { } viewId ? new ConfiguredLogicViewBinding(viewId, entity.ViewRevision!.Value,
                entity.ViewConnectionId!.Value, entity.ViewRole!) : null);

    private static ConfiguredLogicAssetBinding ToDomain(
        ConfiguredLogicAssetBindingEntity entity) => new(
            entity.Role,
            entity.AssetId,
            entity.ParameterOverridesJson);

    private static ConfiguredLogicDataBinding ToDomain(
        ConfiguredLogicDataBindingEntity entity) => new(
            entity.RequirementId,
            entity.AssetId,
            entity.ProviderId,
            entity.ProviderAssetExternalId,
            entity.ProviderMappingJson);
}
