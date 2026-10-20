using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Connections;

public sealed class ConnectionRepository(WellSiteAutoPilotDbContext dbContext, TimeProvider? clock = null) : IConnectionRepository
{
    private const string NameIndex = "IX_connections_NormalizedName";
    private TimeProvider Time => clock ?? TimeProvider.System;

    public async Task<IReadOnlyList<ConnectionRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        (await dbContext.Connections.AsNoTracking().OrderBy(item => item.NormalizedName).ToArrayAsync(cancellationToken))
        .Select(ToRecord).ToArray();

    public async Task<ConnectionRecord?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Connections.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public Task CreateAuthorizedAsync(ConnectionRecord connection, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return WriteAsync(subject, actor, () =>
        {
            dbContext.Connections.Add(new ConnectionEntity
            {
                Id = connection.Id, Name = connection.Name, NormalizedName = Normalize(connection.Name),
                AdapterKey = connection.AdapterKey, SettingsVersion = connection.SettingsVersion,
                SettingsJson = JsonSerializer.Serialize(connection.Settings), IsEnabled = connection.IsEnabled,
                Revision = 1, CreatedAtUtc = connection.CreatedAtUtc, UpdatedAtUtc = connection.CreatedAtUtc
            });
            Audit("connection.created", connection.Id, subject, actor,
                new { connection.AdapterKey, SettingNames = connection.Settings.Keys.Order(StringComparer.Ordinal) });
            return Task.CompletedTask;
        }, cancellationToken);
    }

    public async Task<ConnectionRecord> UpdateAuthorizedAsync(Guid id, int expectedRevision, string name, int settingsVersion,
        IReadOnlyDictionary<string, string> settings, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await WriteAsync(subject, actor, async () =>
        {
            var entity = await LockAsync(id, expectedRevision, cancellationToken);
            entity.Name = name;
            entity.NormalizedName = Normalize(name);
            entity.SettingsVersion = settingsVersion;
            entity.SettingsJson = JsonSerializer.Serialize(settings);
            Touch(entity);
            Audit("connection.updated", id, subject, actor, new { entity.AdapterKey, SettingNames = settings.Keys.Order(StringComparer.Ordinal) });
        }, cancellationToken);
        return ToRecord(await Reload(id, cancellationToken));
    }

    public async Task<ConnectionRecord> SetEnabledAuthorizedAsync(Guid id, int expectedRevision, bool enabled,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        await WriteAsync(subject, actor, async () =>
        {
            var entity = await LockAsync(id, expectedRevision, cancellationToken);
            if (entity.IsEnabled == enabled) return;
            entity.IsEnabled = enabled;
            Touch(entity);
            Audit(enabled ? "connection.enabled" : "connection.disabled", id, subject, actor, new { entity.AdapterKey });
        }, cancellationToken);
        return ToRecord(await Reload(id, cancellationToken));
    }

    public async Task<ConnectionRecord> RecordTestAuthorizedAsync(Guid id, ConnectionTestRecord result,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        await WriteAsync(subject, actor, async () =>
        {
            var entity = await LockAsync(id, null, cancellationToken);
            // A test of settings that have since changed says nothing about the current ones.
            if (entity.Revision != result.TestedRevision) return;
            entity.LastTestOutcome = result.Outcome;
            entity.LastTestMessage = result.Message;
            entity.LastTestActingAs = result.ActingAs;
            entity.LastTestAtUtc = result.TestedAtUtc;
            entity.LastTestRevision = result.TestedRevision;
            Audit("connection.tested", id, subject, actor, new { entity.AdapterKey, result.Outcome });
        }, cancellationToken);
        return ToRecord(await Reload(id, cancellationToken));
    }

    public Task RecordDiscoveryAuthorizedAsync(Guid id, int found, int warnings, bool complete, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default) =>
        WriteAsync(subject, actor, async () =>
        {
            var entity = await LockAsync(id, null, cancellationToken);
            Audit("connection.discovered", id, subject, actor, new { entity.AdapterKey, Found = found, Warnings = warnings, Complete = complete });
        }, cancellationToken);

    public Task DeleteAuthorizedAsync(Guid id, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default) =>
        WriteAsync(subject, actor, async () =>
        {
            // Source bindings do not exist yet; once they do, a connection they reference must refuse deletion here.
            var entity = await LockAsync(id, null, cancellationToken);
            dbContext.Connections.Remove(entity);
            Audit("connection.deleted", id, subject, actor, new { entity.AdapterKey });
        }, cancellationToken);

    private async Task<ConnectionEntity> Reload(Guid id, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        return await dbContext.Connections.AsNoTracking().SingleAsync(item => item.Id == id, cancellationToken);
    }

    private async Task<ConnectionEntity> LockAsync(Guid id, int? expectedRevision, CancellationToken cancellationToken)
    {
        var entity = (await dbContext.Connections.FromSqlInterpolated($"""
            SELECT c.* FROM integration.connections AS c WHERE c."Id" = {id} FOR UPDATE OF c
            """).ToArrayAsync(cancellationToken)).SingleOrDefault()
            ?? throw new WellSiteAutoPilotException(ConnectionFailureCodes.NotFound, FailureKind.NotFound,
                "The connection was not found.");
        if (expectedRevision is { } expected && expected != entity.Revision)
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.StaleRevision, FailureKind.Conflict,
                "The connection was changed by someone else. Reload it and try again.");
        return entity;
    }

    private void Touch(ConnectionEntity entity)
    {
        entity.Revision++;
        entity.UpdatedAtUtc = Time.GetUtcNow();
    }

    private async Task WriteAsync(AccessSubject subject, SecurityActorContext actor, Func<Task> stage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.UserId != subject.UserId || string.IsNullOrWhiteSpace(actor.IdentityName) || actor.IdentityName.Length > 256)
            throw new WellSiteAutoPilotException("SECURITY_REQUEST_SUBJECT_INVALID", FailureKind.Authorization,
                "A bound authenticated actor is required.");
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await GrantWriteGuard.RequireDeploymentAsync(dbContext, subject, WellSitePermissions.ConnectionsManage, Time,
                cancellationToken);
            await stage();
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: NameIndex })
        {
            dbContext.ChangeTracker.Clear();
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.NameConflict, FailureKind.Conflict,
                "Another connection already has that name.", innerException: exception);
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            dbContext.ChangeTracker.Clear();
            throw new WellSiteAutoPilotException("SECURITY_ACCESS_CONFLICT", FailureKind.Conflict,
                "Access or the connection changed concurrently. Reload before retrying.", innerException: exception);
        }
    }

    private void Audit(string action, Guid id, AccessSubject subject, SecurityActorContext actor, object details) =>
        dbContext.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(), OccurredAtUtc = Time.GetUtcNow(), ActorUserId = subject.UserId,
            ActorIdentity = actor.IdentityName.Trim(), CorrelationId = actor.CorrelationId, Action = action,
            TargetType = "connection", TargetId = id.ToString(), AssetId = null, DetailsJson = JsonSerializer.Serialize(details)
        });

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();

    private static ConnectionRecord ToRecord(ConnectionEntity entity) => new(entity.Id, entity.Name, entity.AdapterKey,
        entity.SettingsVersion, JsonSerializer.Deserialize<Dictionary<string, string>>(entity.SettingsJson) ?? [],
        entity.IsEnabled, entity.Revision, entity.CreatedAtUtc, entity.UpdatedAtUtc,
        entity.LastTestOutcome is null || entity.LastTestAtUtc is null || entity.LastTestRevision is null
            ? null
            : new ConnectionTestRecord(entity.LastTestOutcome, entity.LastTestMessage ?? string.Empty,
                entity.LastTestActingAs, entity.LastTestAtUtc.Value, entity.LastTestRevision.Value));
}
