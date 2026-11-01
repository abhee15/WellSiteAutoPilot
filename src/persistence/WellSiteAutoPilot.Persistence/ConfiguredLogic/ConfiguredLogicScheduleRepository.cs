using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed class ConfiguredLogicScheduleRepository(WellSiteAutoPilotDbContext dbContext)
    : IConfiguredLogicScheduleRepository
{
    private const int MaxReasonLength = 1024;
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
        Validate(request);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // The same row lock revision activation takes, so a change and an activation never interleave.
        var definition = await dbContext.ConfiguredLogicDefinitions.FromSqlInterpolated(
                $"SELECT * FROM logic.configured_logic WHERE \"Id\" = {request.ConfiguredLogicId} FOR UPDATE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new WellSiteAutoPilotException(
                "CONFIGURED_LOGIC_NOT_FOUND", FailureKind.NotFound, "The configured logic does not exist.");
        if (definition.ActiveRevisionId != request.ExpectedRevisionId) throw Conflict();

        var current = await dbContext.ConfiguredLogicScheduleStates
            .SingleOrDefaultAsync(item => item.ConfiguredLogicId == request.ConfiguredLogicId, cancellationToken);
        var currentVersion = current is null ? 0 : current.Version;
        if (currentVersion != request.ExpectedVersion) throw Conflict();
        if (current is not null && current.BasedOnRevisionId != request.ExpectedRevisionId) throw Conflict();

        var version = currentVersion + 1;
        if (current is null)
        {
            current = new ConfiguredLogicScheduleStateEntity { ConfiguredLogicId = request.ConfiguredLogicId };
            dbContext.ConfiguredLogicScheduleStates.Add(current);
        }

        current.BasedOnRevisionId = request.ExpectedRevisionId;
        current.Version = version;
        current.Enabled = request.Schedule.Enabled;
        current.CadenceSeconds = request.Schedule.CadenceSeconds;
        current.StartAtUtc = request.Schedule.StartAtUtc;
        current.TimeZoneId = request.Schedule.TimeZoneId;
        current.ChangedBy = request.ChangedBy;
        current.ChangedAtUtc = request.ChangedAtUtc;
        current.Reason = request.Reason;

        dbContext.ConfiguredLogicScheduleChanges.Add(new ConfiguredLogicScheduleChangeEntity
        {
            Id = Guid.NewGuid(),
            ConfiguredLogicId = request.ConfiguredLogicId,
            BasedOnRevisionId = request.ExpectedRevisionId,
            Version = version,
            Kind = request.Kind.ToString(),
            Enabled = request.Schedule.Enabled,
            CadenceSeconds = request.Schedule.CadenceSeconds,
            StartAtUtc = request.Schedule.StartAtUtc,
            TimeZoneId = request.Schedule.TimeZoneId,
            ChangedBy = request.ChangedBy,
            ChangedAtUtc = request.ChangedAtUtc,
            Reason = request.Reason
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToState(current);
    }

    private static void Validate(ConfiguredLogicScheduleChangeRequest request)
    {
        if (request.Kind == ConfiguredLogicScheduleChangeKind.Reset)
            throw Invalid("Reset is recorded by revision activation, not requested directly.");
        if (request.Schedule.CadenceSeconds <= 0) throw Invalid("The cadence must be positive.");
        if (string.IsNullOrWhiteSpace(request.Schedule.TimeZoneId)) throw Invalid("A time zone is required.");
        if (request.Schedule.TimeZoneId.Length > 64) throw Invalid("The time zone is too long.");
        if (string.IsNullOrWhiteSpace(request.ChangedBy)) throw Invalid("Who made the change is required.");
        if (request.ChangedBy.Length > 256) throw Invalid("The actor name is too long.");
        if (request.ExpectedVersion < 0) throw Invalid("The expected version cannot be negative.");
        if (request.Reason is { Length: > MaxReasonLength }) throw Invalid("The reason is too long.");
    }

    private static WellSiteAutoPilotException Invalid(string message) =>
        new("SCHEDULE_CHANGE_INVALID", FailureKind.Validation, message);

    private static WellSiteAutoPilotException Conflict() => new(
        "SCHEDULE_CHANGED", FailureKind.Conflict,
        "The schedule or the live revision changed. Refresh and review before retrying.");

    private static ConfiguredLogicSchedule ToSchedule(bool enabled, int cadenceSeconds, DateTimeOffset startAtUtc,
        string timeZoneId) => new(enabled, cadenceSeconds, startAtUtc, timeZoneId);

    private static ConfiguredLogicScheduleState ToState(ConfiguredLogicScheduleStateEntity entity) => new(
        entity.ConfiguredLogicId, entity.BasedOnRevisionId, entity.Version,
        ToSchedule(entity.Enabled, entity.CadenceSeconds, entity.StartAtUtc, entity.TimeZoneId),
        entity.ChangedBy, entity.ChangedAtUtc, entity.Reason);

    private static ConfiguredLogicScheduleChange ToChange(ConfiguredLogicScheduleChangeEntity entity) => new(
        entity.Id, entity.ConfiguredLogicId, entity.BasedOnRevisionId, entity.Version,
        Enum.Parse<ConfiguredLogicScheduleChangeKind>(entity.Kind),
        ToSchedule(entity.Enabled, entity.CadenceSeconds, entity.StartAtUtc, entity.TimeZoneId),
        entity.ChangedBy, entity.ChangedAtUtc, entity.Reason);
}
