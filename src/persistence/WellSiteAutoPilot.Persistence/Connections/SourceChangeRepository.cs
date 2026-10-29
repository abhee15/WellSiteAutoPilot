using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Connections.Importing;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Connections;

public sealed class SourceChangeRepository(WellSiteAutoPilotDbContext db, TimeProvider? clock = null) : ISourceChangeRepository
{
    private static readonly string[] StaleWarnings = ["Connection settings, accepted source baselines or hierarchy changed during this check. Check again; existing findings are retained."];
    private static readonly string[] FailedWarnings = ["The source check did not complete. Existing findings are retained."];
    private TimeProvider Time => clock ?? TimeProvider.System;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<SourceCheckSummary> BeginAsync(Guid connectionId, int expectedRevision, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken) => WriteAsync(connectionId, subject, actor, async connection =>
    {
        if (!connection.IsEnabled || connection.Revision != expectedRevision) throw Stale();
        var latest = await db.SourceChecks.Where(check => check.ConnectionId == connectionId)
            .OrderByDescending(check => check.Revision).FirstOrDefaultAsync(cancellationToken);
        var now = Time.GetUtcNow();
        if (latest is { Status: "Checking" })
        {
            if (latest.StartedAtUtc > now.AddMinutes(-15)) throw new WellSiteAutoPilotException(
                "SOURCE_CHECK_IN_PROGRESS", FailureKind.Conflict, "A source check is already running. Reload its status before retrying.");
            latest.Status = "Abandoned";
            latest.CompletedAtUtc = now;
        }
        var check = new SourceCheckEntity { Id = Guid.NewGuid(), ConnectionId = connectionId,
            Revision = (latest?.Revision ?? 0) + 1, ConnectionRevision = expectedRevision,
            HierarchyRevision = await db.ImportHierarchies.Where(h => h.ConnectionId == connectionId)
                .Select(h => (int?)h.Revision).SingleOrDefaultAsync(cancellationToken),
            AcceptedBindingsFingerprint = await BindingFingerprintAsync(connectionId, cancellationToken),
            StartedAtUtc = now, ActorUserId = subject.UserId };
        db.SourceChecks.Add(check);
        Audit(check, actor, "connection.source-check.started");
        return Summary(check);
    }, cancellationToken);

    public Task<SourceCheckSummary> CompleteAsync(Guid connectionId, Guid checkId, DiscoveryResult? result,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken) => WriteAsync(connectionId, subject, actor, async connection =>
    {
        var check = await db.SourceChecks.SingleOrDefaultAsync(c => c.Id == checkId && c.ConnectionId == connectionId, cancellationToken)
            ?? throw Stale();
        if (check.ActorUserId != subject.UserId || check.Status != "Checking" ||
            await db.SourceChecks.AnyAsync(c => c.ConnectionId == connectionId && c.Revision > check.Revision, cancellationToken)) throw Stale();
        check.CompletedAtUtc = Time.GetUtcNow();
        if (connection.Revision != check.ConnectionRevision || !connection.IsEnabled ||
            check.AcceptedBindingsFingerprint != await BindingFingerprintAsync(connectionId, cancellationToken) ||
            check.HierarchyRevision != await db.ImportHierarchies.Where(h => h.ConnectionId == connectionId)
                .Select(h => (int?)h.Revision).SingleOrDefaultAsync(cancellationToken))
        {
            check.Status = "Stale";
            check.WarningsJson = Serialize(StaleWarnings);
        }
        else if (result is null)
        {
            check.Status = "Failed";
            check.WarningsJson = Serialize(FailedWarnings);
        }
        else
        {
            check.Status = result.Complete ? "Complete" : "Partial";
            check.ActingAs = result.ActingAs is { Length: > 256 } identity ? identity[..256] : result.ActingAs;
            check.Found = result.Items.Count;
            check.UnavailableFieldsJson = result.UnavailableFields is null ? null : Serialize(result.UnavailableFields);
            var warnings = result.Warnings.ToList();
            check.SourceScopeJson = result.SourceScope is null ? null : Serialize(result.SourceScope.Distinct().Order(StringComparer.Ordinal).ToArray());
            var previous = await db.SourceChecks.AsNoTracking().Where(c => c.ConnectionId == connectionId && c.Status == "Complete")
                .OrderByDescending(c => c.Revision).FirstOrDefaultAsync(cancellationToken);
            if (previous is not null && previous.ActingAs != check.ActingAs)
                warnings.Add("The Gateway source account changed. Review access coverage; wells not returned are not confirmed deleted.");
            if (previous is not null && previous.SourceScopeJson != check.SourceScopeJson)
                warnings.Add("The reported source asset scope changed or is no longer available. Review coverage before interpreting wells not returned.");
            // Ambiguous duplicate identities must not invent a change from whichever row happened to arrive first.
            var duplicates = result.Items.GroupBy(Key).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
            if (duplicates.Count > 0) { check.Status = "Partial"; warnings.Add("Duplicate source identities were excluded from comparison."); }
            check.WarningsJson = Serialize(warnings);
            await RecordEvidenceAsync(check, result, duplicates, cancellationToken);
        }
        Audit(check, actor, "connection.source-check.completed");
        return Summary(check);
    }, cancellationToken);

    private async Task RecordEvidenceAsync(SourceCheckEntity check, DiscoveryResult result, HashSet<string> duplicates, CancellationToken ct)
    {
        var bindings = await db.SourceBindings.AsNoTracking().Where(b => b.ConnectionId == check.ConnectionId)
            .Select(b => new { b.AssetId, b.ResourceKind, b.ExternalId, b.SnapshotJson }).ToArrayAsync(ct);
        var accepted = bindings.ToDictionary(b => BindingKey(b.ResourceKind, b.ExternalId), StringComparer.Ordinal);
        var found = result.Items.Where(item => !duplicates.Contains(Key(item))).ToDictionary(Key, StringComparer.Ordinal);
        var pending = new List<SourceChangeEntity>();
        foreach (var key in accepted.Keys.Union(found.Keys).Order(StringComparer.Ordinal))
        {
            if (duplicates.Contains(key)) { check.ComparisonUnavailable++; continue; }
            accepted.TryGetValue(key, out var binding);
            found.TryGetValue(key, out var item);
            var comparison = SourceChangeComparison.Compare(binding?.SnapshotJson, item, check.Status == "Complete", result.UnavailableFields);
            if (comparison.Status == "ComparisonUnavailable" || comparison.UnknownFields.Count > 0) check.ComparisonUnavailable++;
            if (comparison.Status is "ComparisonUnavailable" or "Unchanged") continue;
            if (comparison.Status == "New") check.New++;
            else if (comparison.Status == "Changed") check.Changed++;
            else if (comparison.Status == "NotReturned") check.NotReturned++;
            var sourceName = item?.DisplayName ?? ImportedSourceBaseline.Read(binding!.SnapshotJson)?.SourceName ?? binding!.ExternalId;
            pending.Add(new SourceChangeEntity { Id = Guid.NewGuid(), ConnectionId = check.ConnectionId, AssetId = binding?.AssetId,
                ResourceKind = binding?.ResourceKind ?? item!.ProviderType, ExternalId = binding?.ExternalId ?? item!.ExternalId,
                SourceName = sourceName.Length <= 256 ? sourceName : sourceName[..256], Fingerprint = comparison.Fingerprint,
                Status = comparison.Status, EvidenceJson = Serialize(comparison), AcceptedBaselineJson = binding?.SnapshotJson,
                ObservedFactsJson = item is null ? null : Serialize(new { item.DisplayName, item.Metadata }),
                FirstCheckId = check.Id, LastCheckId = check.Id, FirstSeenAtUtc = check.CompletedAtUtc!.Value,
                LastSeenAtUtc = check.CompletedAtUtc.Value });
        }
        var fingerprints = pending.Select(c => c.Fingerprint).Distinct().ToArray();
        var existing = await db.SourceChanges.AsNoTracking().Where(c => c.ConnectionId == check.ConnectionId && fingerprints.Contains(c.Fingerprint))
            .Select(c => new { c.Id, c.ResourceKind, c.ExternalId, c.Fingerprint }).ToArrayAsync(ct);
        var byKey = existing.ToDictionary(c => BindingKey(c.ResourceKind, c.ExternalId) + c.Fingerprint, c => c.Id, StringComparer.Ordinal);
        foreach (var change in pending)
        {
            if (byKey.TryGetValue(BindingKey(change.ResourceKind, change.ExternalId) + change.Fingerprint, out var id))
            {
                var stub = new SourceChangeEntity { Id = id, LastCheckId = check.Id, LastSeenAtUtc = check.CompletedAtUtc!.Value };
                db.SourceChanges.Attach(stub);
                db.Entry(stub).Property(c => c.LastCheckId).IsModified = true;
                db.Entry(stub).Property(c => c.LastSeenAtUtc).IsModified = true;
            }
            else { db.SourceChanges.Add(change); byKey.Add(BindingKey(change.ResourceKind, change.ExternalId) + change.Fingerprint, change.Id); }
        }
        // Retained findings acquire references created since the original check, even if the source has reverted.
        // Resolve from pinned target AND input bindings, never by current View membership.
        await db.SaveChangesAsync(ct);
        var impacted = await (from change in db.SourceChanges.AsNoTracking()
            where change.ConnectionId == check.ConnectionId && change.AssetId != null
            from binding in db.ConfiguredLogicAssetBindings.AsNoTracking()
            where binding.AssetId == change.AssetId
            select new { ChangeId = change.Id, binding.RevisionId, Kind = "Target", Reference = binding.Role })
            .Union(from change in db.SourceChanges.AsNoTracking()
                where change.ConnectionId == check.ConnectionId && change.AssetId != null
                from input in db.ConfiguredLogicDataBindings.AsNoTracking()
                where input.AssetId == change.AssetId
                select new { ChangeId = change.Id, input.RevisionId, Kind = "Input", Reference = input.RequirementId })
            .Where(pair => !db.SourceChangeImpacts.Any(saved => saved.ChangeId == pair.ChangeId && saved.RevisionId == pair.RevisionId &&
                saved.Kind == pair.Kind && saved.Reference == pair.Reference)).ToArrayAsync(ct);
        foreach (var pair in impacted) db.SourceChangeImpacts.Add(new SourceChangeImpactEntity { ChangeId = pair.ChangeId,
            RevisionId = pair.RevisionId, Kind = pair.Kind, Reference = pair.Reference, FirstSeenAtUtc = check.CompletedAtUtc!.Value });
    }

    public async Task<SourceChangePage> ReadAsync(Guid connectionId, int offset, int limit, string? query,
        AccessSubject subject, CancellationToken cancellationToken)
    {
        var now = Time.GetUtcNow();
        if (!await ScopedAccessQueries.DeploymentGrants(db, subject.UserId, WellSitePermissions.ConnectionsRead, subject.Groups, now).AnyAsync(cancellationToken) ||
            !await ScopedAccessQueries.ResourceGrants(db, subject.UserId, WellSitePermissions.AssetsRead, subject.Groups, now)
                .AnyAsync(g => g.ScopeKind == "AllAssets", cancellationToken)) throw Denied();
        if (!await db.Connections.AnyAsync(c => c.Id == connectionId, cancellationToken)) throw Missing();
        var checks = db.SourceChecks.AsNoTracking().Where(c => c.ConnectionId == connectionId);
        var latest = await checks.OrderByDescending(c => c.Revision).FirstOrDefaultAsync(cancellationToken);
        var successful = await checks.Where(c => c.Status == "Complete").OrderByDescending(c => c.Revision).FirstOrDefaultAsync(cancellationToken);
        var changes = db.SourceChanges.AsNoTracking().Where(c => c.ConnectionId == connectionId);
        if (!string.IsNullOrEmpty(query))
        {
            var pattern = "%" + query.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            changes = changes.Where(c => EF.Functions.ILike(c.SourceName, pattern, "\\") || EF.Functions.ILike(c.ExternalId, pattern, "\\"));
        }
        var total = await changes.CountAsync(cancellationToken);
        var page = await changes.OrderByDescending(c => c.LastSeenAtUtc).ThenBy(c => c.Id).Skip(offset).Take(limit).ToArrayAsync(cancellationToken);
        var ids = page.Select(c => c.Id).ToArray();
        var assetIds = page.Where(c => c.AssetId != null).Select(c => c.AssetId!.Value).ToArray();
        var readable = ScopedAccessQueries.ConfiguredLogic(db, subject.UserId, WellSitePermissions.ConfiguredLogicRead, subject.Groups, now);
        var references = await (from impact in db.SourceChangeImpacts.AsNoTracking()
            where ids.Contains(impact.ChangeId)
            join revision in db.ConfiguredLogicRevisions.AsNoTracking() on impact.RevisionId equals revision.Id
            join definition in readable on revision.ConfiguredLogicId equals definition.Id
            select new { impact.ChangeId, definition.Id, definition.Name, RevisionId = revision.Id, revision.RevisionNumber,
                Status = definition.ActiveRevisionId == revision.Id ? "Active" : revision.Status, impact.Kind, impact.Reference })
            .ToArrayAsync(cancellationToken);
        // Only Connection Views are included; they follow Connection read access.
        var views = await (from member in db.AssetViewMembers.AsNoTracking()
            where assetIds.Contains(member.AssetId)
            join view in db.AssetViews.AsNoTracking() on member.ViewId equals view.Id
            where view.ConnectionId == connectionId
            select new { member.AssetId, view.Id, view.Name }).ToArrayAsync(cancellationToken);
        var viewLookup = views.OrderBy(v => v.Name, StringComparer.Ordinal).ThenBy(v => v.Id).ToLookup(v => v.AssetId);
        var referenceLookup = references.OrderByDescending(r => r.Status == "Active").ThenBy(r => r.Name, StringComparer.Ordinal)
            .ThenByDescending(r => r.RevisionNumber).ThenBy(r => r.Kind, StringComparer.Ordinal).ThenBy(r => r.Reference, StringComparer.Ordinal).ToLookup(r => r.ChangeId);
        return new(latest is null ? null : Summary(latest), successful is null ? null : Summary(successful), total, offset,
            page.Select(c => new SourceChangeItem(c.Id, c.AssetId, c.ResourceKind, c.ExternalId, c.SourceName, c.Status,
                c.FirstSeenAtUtc, c.LastSeenAtUtc, JsonSerializer.Deserialize<SourceComparison>(c.EvidenceJson, Json)!,
                viewLookup[c.AssetId ?? Guid.Empty].Take(100).Select(v => new SourceReference(v.Id, v.Name, null, null, "Current", "View", "Member")).ToArray(),
                referenceLookup[c.Id].Take(100).Select(r => new SourceReference(r.Id, r.Name, r.RevisionId, r.RevisionNumber,
                    r.Status, r.Kind, r.Reference)).ToArray(),
                viewLookup[c.AssetId ?? Guid.Empty].Count() > 100 || referenceLookup[c.Id].Count() > 100)).ToArray());
    }

    private async Task<T> WriteAsync<T>(Guid connectionId, AccessSubject subject, SecurityActorContext actor,
        Func<ConnectionEntity, Task<T>> action, CancellationToken ct)
    {
        if (subject.UserId != actor.UserId || string.IsNullOrWhiteSpace(actor.IdentityName) || actor.IdentityName.Length > 256) throw Denied();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await AuthorityAsync(subject, ct);
            var connection = (await db.Connections.FromSqlInterpolated($"""
                SELECT c.* FROM integration.connections AS c WHERE c."Id" = {connectionId} FOR UPDATE OF c
                """).AsNoTracking().ToArrayAsync(ct)).SingleOrDefault() ?? throw Missing();
            var result = await action(connection);
            await AuthorityAsync(subject, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception error) when (error.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation)
        {
            db.ChangeTracker.Clear();
            throw new WellSiteAutoPilotException("SOURCE_CHECK_CONFLICT", FailureKind.Conflict,
                "Source-check state or access changed concurrently. Reload before retrying.", innerException: error);
        }
    }
    private async Task<string> BindingFingerprintAsync(Guid connectionId, CancellationToken ct)
    {
        var bindings = await db.SourceBindings.AsNoTracking().Where(b => b.ConnectionId == connectionId)
            .OrderBy(b => b.Id).Select(b => new { b.Id, b.AssetId, b.ResourceKind, b.ExternalId, b.SnapshotJson }).ToArrayAsync(ct);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(bindings, Json)));
    }
    private async Task AuthorityAsync(AccessSubject subject, CancellationToken ct)
    {
        await GrantWriteGuard.RequireDeploymentAsync(db, subject, WellSitePermissions.ConnectionsManage, Time, ct);
        await GrantWriteGuard.RequireAllAssetsAsync(db, subject, WellSitePermissions.AssetsManage, Time, ct);
    }
    private void Audit(SourceCheckEntity check, SecurityActorContext actor, string action) => db.AuditEvents.Add(new AuditEventEntity {
        Id = Guid.NewGuid(), OccurredAtUtc = Time.GetUtcNow(), ActorUserId = actor.UserId, ActorIdentity = actor.IdentityName,
        Action = action, TargetType = "Connection", TargetId = check.ConnectionId.ToString(), CorrelationId = actor.CorrelationId,
        DetailsJson = Serialize(new { check.Id, check.Revision, check.Status, check.Found, check.Changed, check.New, check.NotReturned }) });
    private static SourceCheckSummary Summary(SourceCheckEntity c) => new(c.Id, c.Revision, c.ConnectionRevision, c.Status,
        c.StartedAtUtc, c.CompletedAtUtc, c.ActingAs, c.Found, c.Changed, c.New, c.NotReturned, c.ComparisonUnavailable,
        JsonSerializer.Deserialize<string[]>(c.WarningsJson, Json) ?? [], c.UnavailableFieldsJson is null ? null : JsonSerializer.Deserialize<string[]>(c.UnavailableFieldsJson, Json),
        c.SourceScopeJson is null ? null : JsonSerializer.Deserialize<string[]>(c.SourceScopeJson, Json)!.Length,
        c.SourceScopeJson is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(c.SourceScopeJson))));
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static string Key(DiscoveredAsset item) => BindingKey(item.ProviderType, item.ExternalId);
    private static string BindingKey(string kind, string externalId) => IConnectionImportRepository.BindingKey(kind, externalId);
    private static WellSiteAutoPilotException Denied() => new("SECURITY_SCOPE_DENIED", FailureKind.Authorization,
        "Source review requires Connection read and read access to all Assets; checking requires Connection management and management of all Assets.");
    private static WellSiteAutoPilotException Stale() => new("SOURCE_CHECK_STALE", FailureKind.Conflict, "The source check is stale. Reload and check again.");
    private static WellSiteAutoPilotException Missing() => new(ConnectionFailureCodes.NotFound, FailureKind.NotFound, "The Connection was not found.");
}
