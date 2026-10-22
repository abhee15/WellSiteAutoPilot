using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.ConfiguredLogic;

public sealed partial class ConfiguredLogicRepository
{
    public Task AddAuthorizedAsync(ConfiguredLogicDefinition definition, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Id == Guid.Empty || definition.ActiveRevisionId is not null ||
            string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > 256 ||
            definition.Name.Any(char.IsControl) || definition.Revisions.Count != 1)
            throw InvalidDraft();
        var revision = definition.Revisions.Single();
        ValidateDraft(revision);
        if (revision.ConfiguredLogicId != definition.Id || revision.RevisionNumber != 1) throw InvalidDraft();
        return WriteConfigurationAuthorizedAsync(subject, actor, async () =>
        {
            await RequireConfigurationAssetsAsync(subject, DraftAssets(revision), cancellationToken);
            await RequireViewSnapshotAsync(revision, subject, cancellationToken);
            dbContext.ConfiguredLogicDefinitions.Add(new ConfiguredLogicEntity
            {
                Id = definition.Id, Name = definition.Name, CreatedAtUtc = definition.CreatedAtUtc
            });
            AddRevision(revision);
            Audit(revision, "CONFIGURED_LOGIC_CREATED", actor, (clock ?? TimeProvider.System).GetUtcNow());
        }, cancellationToken);
    }

    public Task AddRevisionAuthorizedAsync(ConfiguredLogicRevision revision, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ValidateDraft(revision);
        return WriteConfigurationAuthorizedAsync(subject, actor, async () =>
        {
            // Resolve the full persisted configuration under the same lock used
            // by lifecycle writes. The client cannot replace its old scope with
            // an authorized incoming binding to acquire a foreign configuration.
            var definition = await LockConfigurationAsync(revision.ConfiguredLogicId, cancellationToken);
            var targets = definition.Revisions.SelectMany(DraftAssets)
                .Concat(DraftAssets(revision)).Distinct().Order().ToArray();
            await RequireConfigurationAssetsAsync(subject, targets, cancellationToken);
            if (revision.RevisionNumber != definition.Revisions.Max(item => item.RevisionNumber) + 1) throw Conflict();
            await RequireViewSnapshotAsync(revision, subject, cancellationToken);
            AddRevision(revision);
            Audit(revision, "CONFIGURED_LOGIC_REVISION_CREATED", actor, (clock ?? TimeProvider.System).GetUtcNow());
        }, cancellationToken);
    }

    private async Task<ConfiguredLogicDefinition> LockConfigurationAsync(Guid id, CancellationToken cancellationToken)
    {
        var definitions = await dbContext.ConfiguredLogicDefinitions.FromSqlInterpolated($"""
            SELECT c.* FROM logic.configured_logic AS c WHERE c."Id" = {id} FOR UPDATE OF c
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (definitions.Length != 1) throw DraftDenied();
        var revisions = await dbContext.ConfiguredLogicRevisions.FromSqlInterpolated($"""
            SELECT r.* FROM logic.configured_logic_revisions AS r
            WHERE r."ConfiguredLogicId" = {id} ORDER BY r."Id" FOR UPDATE OF r
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        var ids = revisions.Select(item => item.Id).ToArray();
        var assets = await dbContext.ConfiguredLogicAssetBindings.FromSqlInterpolated($"""
            SELECT b.* FROM logic.configured_logic_asset_bindings AS b
            WHERE b."RevisionId" = ANY({ids}) ORDER BY b."RevisionId", b."Role", b."AssetId" FOR SHARE OF b
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        var inputs = await dbContext.ConfiguredLogicDataBindings.FromSqlInterpolated($"""
            SELECT b.* FROM logic.configured_logic_data_bindings AS b
            WHERE b."RevisionId" = ANY({ids}) ORDER BY b."RevisionId", b."RequirementId", b."AssetId" FOR SHARE OF b
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (revisions.Length == 0 || revisions.Any(item => !assets.Any(binding => binding.RevisionId == item.Id)))
            throw DraftDenied();
        return ToDomain(definitions[0], revisions.OrderBy(item => item.RevisionNumber).Select(item => ToDomain(item,
            assets.Where(binding => binding.RevisionId == item.Id).Select(ToDomain).ToArray(),
            inputs.Where(binding => binding.RevisionId == item.Id).Select(ToDomain).ToArray())).ToArray());
    }

    private async Task RequireViewSnapshotAsync(ConfiguredLogicRevision revision, AccessSubject subject, CancellationToken cancellationToken)
    {
        if (revision.View is not { } source) return;
        await GrantWriteGuard.RequireDeploymentAsync(dbContext, subject, WellSitePermissions.ConnectionsRead,
            clock ?? TimeProvider.System, cancellationToken);
        var views = await dbContext.AssetViews.FromSqlInterpolated($"""
            SELECT v.* FROM assets.asset_views AS v WHERE v."Id" = {source.ViewId} FOR SHARE OF v
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (views.Length != 1 || views[0].ConnectionId != source.ConnectionId || views[0].Revision != source.Revision)
            throw new WellSiteAutoPilotException("ASSET_VIEW_REVISION_STALE", FailureKind.Conflict,
                "The View changed before the assignment was saved. Review its selection again.");
        var expected = await dbContext.AssetViewMembers.Where(member => member.ViewId == source.ViewId)
            .Select(member => member.AssetId).ToArrayAsync(cancellationToken);
        var actual = revision.AssetBindings.Where(binding => string.Equals(binding.Role, source.Role, StringComparison.OrdinalIgnoreCase))
            .Select(binding => binding.AssetId).ToArray();
        if (actual.Length == 0 || !expected.ToHashSet().SetEquals(actual)) throw InvalidDraft();
        var targets = DraftAssets(revision);
        if (await dbContext.Assets.CountAsync(asset => targets.Contains(asset.Id) && asset.OwnerConnectionId == source.ConnectionId,
                cancellationToken) != targets.Length) throw InvalidDraft();
        var connections = await dbContext.Connections.FromSqlInterpolated($"""
            SELECT c.* FROM integration.connections AS c WHERE c."Id" = {source.ConnectionId} FOR SHARE OF c
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (connections.Length != 1 || !connections[0].IsEnabled) throw DependencyConflict();
        if (revision.DataBindings.Count > 0)
        {
            var adapter = connections[0].AdapterKey;
            var sources = await dbContext.SourceBindings.AsNoTracking().Where(binding =>
                binding.ConnectionId == source.ConnectionId && targets.Contains(binding.AssetId))
                .Select(binding => new { binding.AssetId, binding.ExternalId }).ToArrayAsync(cancellationToken);
            var identities = sources.Select(binding => (binding.AssetId, binding.ExternalId)).ToHashSet();
            if (revision.DataBindings.Any(binding => !string.Equals(binding.ProviderId, adapter, StringComparison.OrdinalIgnoreCase) ||
                !identities.Contains((binding.AssetId, binding.ProviderAssetExternalId))))
                throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_VIEW_SOURCE_MISMATCH", FailureKind.Validation,
                    "View data bindings must use this Connection's adapter and each selected Asset's stored source identity.");
        }
    }

    private async Task RequireConfigurationAssetsAsync(AccessSubject subject, Guid[] targets, CancellationToken cancellationToken)
    {
        var time = clock ?? TimeProvider.System;
        await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ConfiguredLogicRead, targets, time, cancellationToken);
        await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ConfiguredLogicManage, targets, time, cancellationToken);
        // The second fence may block. Recheck read proof freshness after it.
        await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ConfiguredLogicRead, targets, time, cancellationToken);
    }

    private async Task WriteConfigurationAuthorizedAsync(AccessSubject subject, SecurityActorContext actor, Func<Task> stage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.UserId != subject.UserId || string.IsNullOrWhiteSpace(actor.IdentityName) ||
            actor.IdentityName.Length > 256 || actor.IdentityName.Any(char.IsControl)) throw DraftDenied();
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await stage();
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation)
        {
            throw new WellSiteAutoPilotException("CONFIGURED_LOGIC_REVISION_CONFLICT", FailureKind.Conflict,
                "Configuration or access changed concurrently. Reload before retrying.", innerException: exception);
        }
    }

    private static Guid[] DraftAssets(ConfiguredLogicRevision revision) => revision.AssetBindings.Select(item => item.AssetId)
        .Concat(revision.DataBindings.Select(item => item.AssetId)).Distinct().Order().ToArray();

    private static void ValidateDraft(ConfiguredLogicRevision revision)
    {
        if (revision.Id == Guid.Empty || revision.ConfiguredLogicId == Guid.Empty || revision.RevisionNumber < 1 ||
            revision.Mode != ExecutionMode.Shadow || revision.Status != ConfiguredLogicRevisionStatus.Draft ||
            revision.ValidatedAtUtc is not null || revision.ActivatedAtUtc is not null ||
            revision.AssetBindings.Count == 0 || DraftAssets(revision).Contains(Guid.Empty) ||
            (revision.View is { } source && (source.ViewId == Guid.Empty || source.ConnectionId == Guid.Empty ||
                source.Revision < 1 || string.IsNullOrWhiteSpace(source.Role) || source.Role.Length > 128))) throw InvalidDraft();
    }

    private static WellSiteAutoPilotException InvalidDraft() => new(FailureCodes.ValidationFailed, FailureKind.Validation,
        "An initial Shadow draft with explicit Asset bindings is required.");
    private static WellSiteAutoPilotException DraftDenied() => new("SECURITY_SCOPE_DENIED", FailureKind.Authorization,
        "The current user is not authorized to modify this configuration.");
}
