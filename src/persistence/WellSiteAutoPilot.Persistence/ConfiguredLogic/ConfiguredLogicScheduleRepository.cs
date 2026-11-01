using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed class ConfiguredLogicScheduleRepository(WellSiteAutoPilotDbContext dbContext)
    : IConfiguredLogicScheduleRepository
{
    private const int MaxChangeListLimit = 200;

    public async Task<ConfiguredLogicScheduleState?> GetAsync(
        Guid configuredLogicId,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.ConfiguredLogicScheduleStates.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ConfiguredLogicId == configuredLogicId, cancellationToken);
        return entity is null ? null : ToState(entity);
    }

    public async Task<IReadOnlyDictionary<Guid, ConfiguredLogicScheduleState>> ListAsync(
        IReadOnlyCollection<Guid> configuredLogicIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuredLogicIds);
        if (configuredLogicIds.Count == 0) return new Dictionary<Guid, ConfiguredLogicScheduleState>();
        var ids = configuredLogicIds.Distinct().ToArray();
        var entities = await dbContext.ConfiguredLogicScheduleStates.AsNoTracking()
            .Where(item => ids.Contains(item.ConfiguredLogicId))
            .ToListAsync(cancellationToken);
        return entities.ToDictionary(item => item.ConfiguredLogicId, ToState);
    }

    public async Task<IReadOnlyList<ConfiguredLogicScheduleChange>> ListChangesAsync(
        Guid configuredLogicId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, MaxChangeListLimit);
        var entities = await dbContext.ConfiguredLogicScheduleChanges.AsNoTracking()
            .Where(item => item.ConfiguredLogicId == configuredLogicId)
            .OrderByDescending(item => item.ChangedAtUtc)
            .ThenByDescending(item => item.Version)
            .Take(take)
            .ToListAsync(cancellationToken);
        return entities.Select(ToChange).ToList();
    }

    public async Task<ConfiguredLogicScheduleState> ApplyAsync(
        ConfiguredLogicScheduleChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // The same row lock revision activation takes, so a change and an activation never interleave.
        var definition = await dbContext.ConfiguredLogicDefinitions.FromSqlInterpolated(
                $"SELECT * FROM logic.configured_logic WHERE \"Id\" = {request.ConfiguredLogicId} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new WellSiteAutoPilotException(
                "CONFIGURED_LOGIC_NOT_FOUND", FailureKind.NotFound, "The configured logic does not exist.");
        var staged = await ConfiguredLogicScheduleWriter.StageAsync(
            dbContext, definition.ActiveRevisionId, request, null, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToState(staged);
    }

    private static ConfiguredLogicSchedule ToSchedule(bool enabled, int cadenceSeconds, DateTimeOffset startAtUtc,
        string timeZoneId) => new(enabled, cadenceSeconds, startAtUtc, timeZoneId);

    internal static ConfiguredLogicScheduleState ToState(ConfiguredLogicScheduleStateEntity entity) => new(
        entity.ConfiguredLogicId, entity.BasedOnRevisionId, entity.Version,
        ToSchedule(entity.Enabled, entity.CadenceSeconds, entity.StartAtUtc, entity.TimeZoneId),
        entity.ChangedBy, entity.ChangedAtUtc, entity.Reason);

    private static ConfiguredLogicScheduleChange ToChange(ConfiguredLogicScheduleChangeEntity entity) => new(
        entity.Id, entity.ConfiguredLogicId, entity.BasedOnRevisionId, entity.Version,
        Enum.Parse<ConfiguredLogicScheduleChangeKind>(entity.Kind),
        ToSchedule(entity.Enabled, entity.CadenceSeconds, entity.StartAtUtc, entity.TimeZoneId),
        entity.ChangedBy, entity.ChangedAtUtc, entity.Reason);
}
