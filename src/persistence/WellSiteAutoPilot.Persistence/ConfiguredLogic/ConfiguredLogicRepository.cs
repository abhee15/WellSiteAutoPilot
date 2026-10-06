using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;

namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed class ConfiguredLogicRepository(
    WellSiteAutoPilotDbContext dbContext) : IConfiguredLogicRepository
{
    public async Task AddAsync(
        ConfiguredLogicDefinition configuredLogic,
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
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddRevisionAsync(
        ConfiguredLogicRevision revision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);

        AddRevision(revision);
        await dbContext.SaveChangesAsync(cancellationToken);
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

    public async Task SetRevisionValidatedAsync(
        Guid configuredLogicId,
        Guid revisionId,
        DateTimeOffset validatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var revision = await dbContext.ConfiguredLogicRevisions
            .SingleAsync(
                item => item.Id == revisionId &&
                        item.ConfiguredLogicId == configuredLogicId,
                cancellationToken);

        revision.Status = ConfiguredLogicRevisionStatus.Validated.ToString();
        revision.ValidatedAtUtc = validatedAtUtc;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ActivateRevisionAsync(
        Guid configuredLogicId,
        Guid revisionId,
        DateTimeOffset activatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var definition = await dbContext.ConfiguredLogicDefinitions
            .SingleAsync(item => item.Id == configuredLogicId, cancellationToken);

        var target = await dbContext.ConfiguredLogicRevisions
            .SingleAsync(
                item => item.Id == revisionId &&
                        item.ConfiguredLogicId == configuredLogicId,
                cancellationToken);

        var activeRevisions = await dbContext.ConfiguredLogicRevisions
            .Where(
                item => item.ConfiguredLogicId == configuredLogicId &&
                        item.Status == nameof(ConfiguredLogicRevisionStatus.Active))
            .ToArrayAsync(cancellationToken);

        foreach (var active in activeRevisions)
        {
            active.Status = ConfiguredLogicRevisionStatus.Superseded.ToString();
        }

        target.Status = ConfiguredLogicRevisionStatus.Active.ToString();
        target.ActivatedAtUtc = activatedAtUtc;
        definition.ActiveRevisionId = target.Id;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private void AddRevision(ConfiguredLogicRevision revision)
    {
        dbContext.ConfiguredLogicRevisions.Add(new ConfiguredLogicRevisionEntity
        {
            Id = revision.Id,
            ConfiguredLogicId = revision.ConfiguredLogicId,
            RevisionNumber = revision.RevisionNumber,
            ModuleId = revision.ModuleId,
            ModuleVersion = revision.ModuleVersion,
            ModuleManifestJson = revision.ModuleManifestJson,
            Mode = revision.Mode.ToString(),
            ParametersJson = revision.ParametersJson,
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
        var bindings = await dbContext.ConfiguredLogicAssetBindings
            .AsNoTracking()
            .Where(item => revisionIds.Contains(item.RevisionId))
            .OrderBy(item => item.Role)
            .ThenBy(item => item.AssetId)
            .ToArrayAsync(cancellationToken);

        return revisions
            .Select(revision => ToDomain(
                revision,
                bindings
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
        IReadOnlyCollection<ConfiguredLogicAssetBinding> bindings) => new(
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
            bindings);

    private static ConfiguredLogicAssetBinding ToDomain(
        ConfiguredLogicAssetBindingEntity entity) => new(
            entity.Role,
            entity.AssetId,
            entity.ParameterOverridesJson);
}
