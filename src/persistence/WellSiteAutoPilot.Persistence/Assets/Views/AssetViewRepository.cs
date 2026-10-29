using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Assets.Views;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Assets;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Assets.Views;

public sealed class AssetViewRepository(WellSiteAutoPilotDbContext dbContext, TimeProvider? clock = null) : IAssetViewRepository
{
    private TimeProvider Time => clock ?? TimeProvider.System;

    // Connection reads currently use a deployment-wide grant, exactly like the Connections page.
    // Keep the predicate here authoritative; owner summaries are not a Connection authorization source.
    private IQueryable<AssetViewEntity> Readable(AccessSubject subject)
    {
        var grants = ScopedAccessQueries.DeploymentGrants(dbContext, subject.UserId, WellSitePermissions.ConnectionsRead,
            subject.Groups, Time.GetUtcNow());
        return dbContext.AssetViews.AsNoTracking().Where(view => view.ConnectionId != null && grants.Any());
    }

    private IQueryable<AssetEntity> VisibleWells(AccessSubject subject) =>
        ScopedAccessQueries.Assets(dbContext, subject.UserId, WellSitePermissions.AssetsRead,
            subject.Groups, Time.GetUtcNow())
            .Where(asset => dbContext.AssetTypes.Any(type => type.Id == asset.AssetTypeId && type.Key == "well" && type.IsActive));

    private IQueryable<AssetEntity> InspectableAssets(AccessSubject subject)
    {
        var allAssets = ScopedAccessQueries.ResourceGrants(dbContext, subject.UserId, WellSitePermissions.AssetsRead,
            subject.Groups, Time.GetUtcNow()).Where(grant => grant.ScopeKind == "AllAssets");
        var visible = ScopedAccessQueries.Assets(dbContext, subject.UserId, WellSitePermissions.AssetsRead,
            subject.Groups, Time.GetUtcNow());
        // Broad Asset readers may inspect inactive member names solely to repair a saved selection.
        // Exact-scope readers still cannot obtain hidden IDs through this maintenance path.
        return dbContext.Assets.AsNoTracking().Where(asset => allAssets.Any() || visible.Any(item => item.Id == asset.Id));
    }

    private IQueryable<AssetViewRecord> Describe(AccessSubject subject, IQueryable<AssetViewEntity> query)
    {
        var visible = VisibleWells(subject);
        var inspectable = InspectableAssets(subject);
        return query.Select(view => new AssetViewRecord(view.Id, view.ConnectionId!.Value, view.Name, view.Revision,
            dbContext.AssetViewMembers.Count(member => member.ViewId == view.Id &&
                visible.Any(asset => asset.Id == member.AssetId && asset.OwnerConnectionId == view.ConnectionId)),
            dbContext.AssetViewMembers.Any(member => member.ViewId == view.Id &&
                !visible.Any(asset => asset.Id == member.AssetId && asset.OwnerConnectionId == view.ConnectionId)),
            !dbContext.AssetViewMembers.Any(member => member.ViewId == view.Id &&
                !inspectable.Any(asset => asset.Id == member.AssetId && asset.OwnerConnectionId == view.ConnectionId)),
            view.CreatedAtUtc, view.UpdatedAtUtc));
    }

    public async Task<IReadOnlyList<AssetViewRecord>> ListAsync(AccessSubject subject, CancellationToken cancellationToken = default) =>
        await Describe(subject, Readable(subject).OrderBy(view => view.NormalizedName).ThenBy(view => view.Id))
            .ToArrayAsync(cancellationToken);

    public Task<AssetViewRecord?> FindAsync(AccessSubject subject, Guid id, CancellationToken cancellationToken = default) =>
        Describe(subject, Readable(subject).Where(view => view.Id == id)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Asset>> MembersAsync(AccessSubject subject, Guid id, string? search, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || limit is < 1 or > 500) throw AssetViewDefinitionRules.Invalid("The page is invalid.");
        var view = await FindAsync(subject, id, cancellationToken) ?? throw NotFound();
        var matches = VisibleWells(subject).Where(asset => asset.OwnerConnectionId == view.ConnectionId &&
                dbContext.AssetViewMembers.Any(member => member.ViewId == id && member.AssetId == asset.Id));
        if (!string.IsNullOrWhiteSpace(search))
        {
            if (search.Length > 120) throw AssetViewDefinitionRules.Invalid("Search is limited to 120 characters.");
            var pattern = "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            matches = matches.Where(asset => EF.Functions.ILike(asset.Name, pattern, "\\"));
        }
        return await matches.OrderBy(asset => asset.Name).ThenBy(asset => asset.Id).Skip(offset).Take(limit)
            .Select(asset => new Asset(asset.Id, asset.AssetTypeId, asset.Name, asset.ParentAssetId,
                asset.AttributeValuesJson, asset.IsActive, asset.CreatedAtUtc, asset.OwnerConnectionId))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<AssetViewSelection> SelectionAsync(AccessSubject subject, Guid id, CancellationToken cancellationToken = default)
    {
        // Read the revision and complete ID set from one snapshot. Do not return a partial replacement selection.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var view = await FindAsync(subject, id, cancellationToken) ?? throw NotFound();
        if (!view.CanInspectAllMembers) throw AssetViewDefinitionRules.Invalid(
            "Some selected wells are inactive or unavailable to you. A complete selection is required to edit or assign this View.");
        var ids = await dbContext.AssetViewMembers.AsNoTracking().Where(member => member.ViewId == id)
            .OrderBy(member => member.AssetId).Select(member => member.AssetId).ToArrayAsync(cancellationToken);
        var eligible = VisibleWells(subject);
        var unavailable = await dbContext.AssetViewMembers.Where(member => member.ViewId == id)
            .Join(dbContext.Assets, member => member.AssetId, asset => asset.Id, (member, asset) => asset)
            .Where(asset => !eligible.Any(well => well.Id == asset.Id && well.OwnerConnectionId == view.ConnectionId))
            .OrderBy(asset => asset.Name).ThenBy(asset => asset.Id)
            .Select(asset => new AssetViewUnavailableMember(asset.Id, asset.Name)).ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AssetViewSelection(view.Revision, ids, unavailable);
    }

    public async Task<AssetViewRecord> CreateAsync(AccessSubject subject, SecurityActorContext actor, Guid connectionId,
        string name, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default)
    {
        name = AssetViewDefinitionRules.NormalizeName(name);
        var ids = AssetViewDefinitionRules.NormalizeMembers(assetIds);
        var id = Guid.NewGuid();
        await WriteAsync(subject, actor, WellSitePermissions.AssetViewsCreate, async () =>
        {
            if (connectionId == Guid.Empty || !await dbContext.Connections.AnyAsync(item => item.Id == connectionId, cancellationToken))
                throw AssetViewDefinitionRules.Invalid("Select one existing Connection.");
            await ValidateMembersAsync(subject, connectionId, ids, cancellationToken);
            var now = Time.GetUtcNow();
            dbContext.AssetViews.Add(new AssetViewEntity { Id = id, ConnectionId = connectionId, OwnerUserId = subject.UserId,
                Name = name, NormalizedName = Normalize(name), Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            dbContext.AssetViewMembers.AddRange(ids.Select(assetId => new AssetViewMemberEntity { ViewId = id, AssetId = assetId }));
            Audit("asset-view.created", id, subject, actor, new { ConnectionId = connectionId, MemberCount = ids.Length, Revision = 1 });
        }, cancellationToken);
        return (await FindAsync(subject, id, cancellationToken))!;
    }

    public async Task<AssetViewRecord> UpdateAsync(AccessSubject subject, SecurityActorContext actor, Guid id, int expectedRevision,
        string name, IReadOnlyList<Guid> assetIds, CancellationToken cancellationToken = default)
    {
        name = AssetViewDefinitionRules.NormalizeName(name);
        var ids = AssetViewDefinitionRules.NormalizeMembers(assetIds);
        await WriteAsync(subject, actor, WellSitePermissions.AssetViewsEdit, async () =>
        {
            var view = await LockAsync(id, expectedRevision, cancellationToken);
            // A restricted reader cannot erase members they cannot inspect, even with a guessed revision.
            var current = await FindAsync(subject, id, cancellationToken) ?? throw NotFound();
            if (!current.CanInspectAllMembers) throw AssetViewDefinitionRules.Invalid("The complete current membership must be available before editing.");
            await ValidateMembersAsync(subject, view.ConnectionId!.Value, ids, cancellationToken);
            var previous = await dbContext.AssetViewMembers.Where(member => member.ViewId == id).ToArrayAsync(cancellationToken);
            var wanted = ids.ToHashSet();
            var existing = previous.Select(member => member.AssetId).ToHashSet();
            dbContext.AssetViewMembers.RemoveRange(previous.Where(member => !wanted.Contains(member.AssetId)));
            dbContext.AssetViewMembers.AddRange(ids.Where(assetId => !existing.Contains(assetId))
                .Select(assetId => new AssetViewMemberEntity { ViewId = id, AssetId = assetId }));
            view.Name = name;
            view.NormalizedName = Normalize(name);
            view.Revision++;
            view.UpdatedAtUtc = Time.GetUtcNow();
            Audit("asset-view.updated", id, subject, actor,
                new { view.ConnectionId, MemberCount = ids.Length, BeforeCount = previous.Length, view.Revision });
        }, cancellationToken);
        return (await FindAsync(subject, id, cancellationToken))!;
    }

    public Task DeleteAsync(AccessSubject subject, SecurityActorContext actor, Guid id, int expectedRevision,
        CancellationToken cancellationToken = default) => WriteAsync(subject, actor, WellSitePermissions.AssetViewsDelete, async () =>
        {
            var view = await LockAsync(id, expectedRevision, cancellationToken);
            if (await dbContext.ConfiguredLogicRevisions.AnyAsync(revision => revision.ViewId == id, cancellationToken))
                throw new WellSiteAutoPilotException("ASSET_VIEW_REFERENCED", FailureKind.Conflict,
                    "This View is referenced by a logic configuration and cannot be deleted.");
            dbContext.AssetViews.Remove(view);
            Audit("asset-view.deleted", id, subject, actor, new { view.ConnectionId, view.Revision });
        }, cancellationToken);

    private async Task ValidateMembersAsync(AccessSubject subject, Guid connectionId, Guid[] ids, CancellationToken cancellationToken)
    {
        // Hold the selected Assets while validating and saving so ownership/activity cannot change mid-write.
        await dbContext.Assets.FromSqlInterpolated($"""
            SELECT a.* FROM assets.assets AS a WHERE a."Id" = ANY({ids}) ORDER BY a."Id" FOR SHARE OF a
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (await VisibleWells(subject).CountAsync(asset => asset.OwnerConnectionId == connectionId && ids.Contains(asset.Id), cancellationToken) != ids.Length)
            throw AssetViewDefinitionRules.Invalid("Every selected item must be an active, authorized well from this Connection.");
    }

    private async Task<AssetViewEntity> LockAsync(Guid id, int expectedRevision, CancellationToken cancellationToken)
    {
        var view = (await dbContext.AssetViews.FromSqlInterpolated($"""
            SELECT v.* FROM assets.asset_views AS v WHERE v."Id" = {id} AND v."ConnectionId" IS NOT NULL FOR UPDATE OF v
            """).ToArrayAsync(cancellationToken)).SingleOrDefault() ?? throw NotFound();
        if (expectedRevision < 1 || view.Revision != expectedRevision)
            throw new WellSiteAutoPilotException(AssetViewFailureCodes.StaleRevision, FailureKind.Conflict,
                "The View changed elsewhere. Reload it before saving.");
        return view;
    }

    private async Task WriteAsync(AccessSubject subject, SecurityActorContext actor, string permission, Func<Task> stage,
        CancellationToken cancellationToken)
    {
        if (actor.UserId != subject.UserId || string.IsNullOrWhiteSpace(actor.IdentityName))
            throw new WellSiteAutoPilotException("SECURITY_REQUEST_SUBJECT_INVALID", FailureKind.Authorization, "An authenticated actor is required.");
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            // Keep a consistent lock order across all View mutations and check both capabilities independently.
            foreach (var required in new[] { WellSitePermissions.ConnectionsRead, permission }.Order(StringComparer.Ordinal))
                await GrantWriteGuard.RequireDeploymentAsync(dbContext, subject, required, Time, cancellationToken);
            await stage();
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_asset_views_ConnectionId_NormalizedName" })
        {
            dbContext.ChangeTracker.Clear();
            throw new WellSiteAutoPilotException(AssetViewFailureCodes.NameConflict, FailureKind.Conflict,
                "A View with that name already exists in this Connection.", innerException: exception);
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            dbContext.ChangeTracker.Clear();
            throw new WellSiteAutoPilotException("ASSET_VIEW_ACCESS_CONFLICT", FailureKind.Conflict,
                "Access or the View changed concurrently. Reload before retrying.", innerException: exception);
        }
    }

    private void Audit(string action, Guid id, AccessSubject subject, SecurityActorContext actor, object details) =>
        dbContext.AuditEvents.Add(new AuditEventEntity { Id = Guid.NewGuid(), OccurredAtUtc = Time.GetUtcNow(),
            ActorUserId = subject.UserId, ActorIdentity = actor.IdentityName, CorrelationId = actor.CorrelationId,
            Action = action, TargetType = "asset-view", TargetId = id.ToString(), DetailsJson = JsonSerializer.Serialize(details) });

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();
    private static WellSiteAutoPilotException NotFound() => new(AssetViewFailureCodes.NotFound, FailureKind.NotFound, "The View was not found.");
}
