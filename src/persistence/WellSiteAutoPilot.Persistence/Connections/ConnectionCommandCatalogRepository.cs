using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Connections;

public sealed class ConnectionCommandCatalogRepository(WellSiteAutoPilotDbContext dbContext, TimeProvider? clock = null)
    : IConnectionCommandCatalogRepository
{
    private const int MaxReasonLength = 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private TimeProvider Time => clock ?? TimeProvider.System;

    public async Task<ConnectionCommandCatalog?> FindAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var header = await dbContext.ConnectionCommandCatalogs.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ConnectionId == connectionId, cancellationToken);
        if (header is null) return null;
        var entries = await dbContext.ConnectionCommandCatalogEntries.AsNoTracking()
            .Where(item => item.ConnectionId == connectionId).OrderBy(item => item.Command).ToArrayAsync(cancellationToken);
        return ToCatalog(header, entries);
    }

    public async Task<ConnectionCommandCatalog> ReplaceAuthorizedAsync(
        Guid connectionId, int expectedVersion, IReadOnlyCollection<ConnectionCommandCatalogEntry> entries, string? reason,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.UserId != subject.UserId || string.IsNullOrWhiteSpace(actor.IdentityName) || actor.IdentityName.Length > 256)
            throw new WellSiteAutoPilotException("SECURITY_REQUEST_SUBJECT_INVALID", FailureKind.Authorization,
                "A bound authenticated actor is required.");
        if (expectedVersion < 0) throw Invalid("The expected version cannot be negative.");
        if (reason is { Length: > MaxReasonLength }) throw Invalid("The reason is too long.");
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var ordered = entries.OrderBy(entry => entry.Command, StringComparer.OrdinalIgnoreCase).ToArray();
        if (ordered.Any(entry => entry is null || string.IsNullOrWhiteSpace(entry.Command) || entry.Command.Length > 128 ||
                entry.Target is null))
            throw Invalid("Every catalog entry needs a command name of at most 128 characters and a target.");
        if (ordered.Select(entry => entry.Command).Distinct(StringComparer.OrdinalIgnoreCase).Count() != ordered.Length)
            throw Invalid("A command is listed more than once.");

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            // Both rights are needed: the catalog is a control decision about a connection.
            await GrantWriteGuard.RequireDeploymentAsync(dbContext, subject, WellSitePermissions.ControlManage, Time, cancellationToken);
            await GrantWriteGuard.RequireDeploymentAsync(dbContext, subject, WellSitePermissions.ConnectionsManage, Time, cancellationToken);

            var locked = (await dbContext.Connections.FromSqlInterpolated($"""
                SELECT c.* FROM integration.connections AS c WHERE c."Id" = {connectionId} FOR UPDATE OF c
                """).AsNoTracking().ToArrayAsync(cancellationToken)).SingleOrDefault()
                ?? throw new WellSiteAutoPilotException(ConnectionFailureCodes.NotFound, FailureKind.NotFound,
                    "The connection was not found.");

            var header = await dbContext.ConnectionCommandCatalogs
                .SingleOrDefaultAsync(item => item.ConnectionId == connectionId, cancellationToken);
            var currentVersion = header?.Version ?? 0;
            if (currentVersion != expectedVersion)
                throw new WellSiteAutoPilotException(ConnectionCommandCatalogFailureCodes.Stale, FailureKind.Conflict,
                    "The command catalog was changed by someone else. Reload it and review before trying again.");

            var version = currentVersion + 1;
            var now = Time.GetUtcNow();
            var changedBy = actor.IdentityName.Trim();
            var before = header is null
                ? []
                : await dbContext.ConnectionCommandCatalogEntries.AsNoTracking()
                    .Where(item => item.ConnectionId == connectionId).ToArrayAsync(cancellationToken);
            await dbContext.ConnectionCommandCatalogEntries.Where(item => item.ConnectionId == connectionId)
                .ExecuteDeleteAsync(cancellationToken);

            if (header is null)
            {
                header = new ConnectionCommandCatalogEntity { ConnectionId = connectionId };
                dbContext.ConnectionCommandCatalogs.Add(header);
            }

            header.Version = version;
            header.ChangedBy = changedBy;
            header.ChangedAtUtc = now;
            header.Reason = normalizedReason;
            foreach (var entry in ordered)
            {
                dbContext.ConnectionCommandCatalogEntries.Add(new ConnectionCommandCatalogEntryEntity
                {
                    ConnectionId = connectionId,
                    Command = entry.Command,
                    Enabled = entry.Enabled,
                    Minimum = entry.Minimum,
                    Maximum = entry.Maximum,
                    MaximumStep = entry.MaximumStep,
                    TargetJson = JsonSerializer.Serialize(
                        new SortedDictionary<string, string>(entry.Target.ToDictionary(pair => pair.Key, pair => pair.Value),
                            StringComparer.Ordinal), Json),
                    SettleSeconds = entry.SettleSeconds,
                    DeadlineSeconds = entry.DeadlineSeconds
                });
            }

            dbContext.ConnectionCommandCatalogChanges.Add(new ConnectionCommandCatalogChangeEntity
            {
                Id = Guid.NewGuid(),
                ConnectionId = connectionId,
                Version = version,
                SnapshotJson = JsonSerializer.Serialize(ordered, Json),
                ChangedBy = changedBy,
                ChangedAtUtc = now,
                Reason = normalizedReason
            });
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = now,
                ActorUserId = subject.UserId,
                ActorIdentity = changedBy,
                CorrelationId = actor.CorrelationId,
                Action = "connection.command-catalog.changed",
                TargetType = "connection",
                TargetId = connectionId.ToString(),
                AssetId = null,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    locked.AdapterKey,
                    version,
                    enabledBefore = before.Where(item => item.Enabled).Select(item => item.Command).Order(StringComparer.Ordinal),
                    enabledAfter = ordered.Where(entry => entry.Enabled).Select(entry => entry.Command).Order(StringComparer.Ordinal)
                })
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected
                or PostgresErrorCodes.UniqueViolation)
        {
            dbContext.ChangeTracker.Clear();
            throw new WellSiteAutoPilotException(ConnectionCommandCatalogFailureCodes.Stale, FailureKind.Conflict,
                "The command catalog or access changed concurrently. Reload it and review before trying again.",
                innerException: exception);
        }

        dbContext.ChangeTracker.Clear();
        return (await FindAsync(connectionId, cancellationToken))!;
    }

    private static ConnectionCommandCatalog ToCatalog(
        ConnectionCommandCatalogEntity header, IReadOnlyCollection<ConnectionCommandCatalogEntryEntity> entries) =>
        new(header.ConnectionId, header.Version, header.ChangedBy, header.ChangedAtUtc, header.Reason,
            entries.Select(item => new ConnectionCommandCatalogEntry(
                item.Command, item.Enabled, item.Minimum, item.Maximum, item.MaximumStep,
                JsonSerializer.Deserialize<Dictionary<string, string>>(item.TargetJson, Json) ?? [],
                item.SettleSeconds, item.DeadlineSeconds)).ToArray());

    private static WellSiteAutoPilotException Invalid(string message) =>
        new(ConnectionCommandCatalogFailureCodes.Invalid, FailureKind.Validation, message);
}
