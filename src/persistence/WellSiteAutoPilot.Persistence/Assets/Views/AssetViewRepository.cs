using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Assets.Views;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Persistence.Assets.Views;

public sealed class AssetViewRepository(WellSiteAutoPilotDbContext dbContext, TimeProvider? clock = null) : IAssetViewRepository
{
    private const string NameIndex = "IX_asset_views_OwnerUserId_NormalizedName";
    private const string SharedNameIndex = "IX_asset_views_NormalizedName_Shared";
    private const int Personal = (int)AssetViewVisibility.Personal;
    private const int Shared = (int)AssetViewVisibility.Shared;
    private TimeProvider Time => clock ?? TimeProvider.System;

    public async Task<IReadOnlyList<AssetViewRecord>> ListAsync(Guid viewerUserId, CancellationToken cancellationToken = default) =>
        (await Readable(viewerUserId).OrderBy(item => item.OwnerUserId == viewerUserId ? 0 : 1)
            .ThenBy(item => item.View.NormalizedName).ThenBy(item => item.View.Id).ToArrayAsync(cancellationToken))
            .Select(ToRecord).ToArray();

    public async Task<AssetViewRecord?> FindAsync(Guid viewerUserId, Guid id, CancellationToken cancellationToken = default)
    {
        var row = await Readable(viewerUserId).SingleOrDefaultAsync(item => item.View.Id == id, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<AssetViewRecord> CreateAsync(Guid ownerUserId, string name, AssetViewDefinition definition,
        AssetViewVisibility visibility, CancellationToken cancellationToken = default)
    {
        var now = Time.GetUtcNow();
        var id = Guid.NewGuid();
        await WriteAsync(async () =>
        {
            if (await dbContext.AssetViews.CountAsync(item => item.OwnerUserId == ownerUserId, cancellationToken) >=
                AssetViewDefinitionRules.MaximumViewsPerPerson)
                throw new WellSiteAutoPilotException(AssetViewFailureCodes.LimitReached, FailureKind.Conflict,
                    "You have reached the limit of 50 views. Delete one you no longer use first.");
            await EnsureSharedRoomAsync(visibility, excluding: null, cancellationToken);
            dbContext.AssetViews.Add(new AssetViewEntity
            {
                Id = id, OwnerUserId = ownerUserId, Name = name, NormalizedName = Normalize(name),
                Visibility = (int)visibility,
                DefinitionJson = JsonSerializer.Serialize(definition), Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            });
        }, cancellationToken);
        return (await FindAsync(ownerUserId, id, cancellationToken))!;
    }

    public async Task<AssetViewRecord> UpdateAsync(Guid ownerUserId, Guid id, int expectedRevision, string name,
        AssetViewDefinition definition, AssetViewVisibility visibility, CancellationToken cancellationToken = default)
    {
        await WriteAsync(async () =>
        {
            var entity = await LockAsync(id, expectedRevision, ownerUserId, cancellationToken);
            await EnsureSharedRoomAsync(visibility, excluding: entity.Id, cancellationToken);
            entity.Name = name;
            entity.NormalizedName = Normalize(name);
            entity.Visibility = (int)visibility;
            entity.DefinitionJson = JsonSerializer.Serialize(definition);
            entity.Revision++;
            entity.UpdatedAtUtc = Time.GetUtcNow();
        }, cancellationToken);
        return (await FindAsync(ownerUserId, id, cancellationToken))!;
    }

    public Task DeleteAsync(Guid callerUserId, bool asAdministrator, Guid id, int expectedRevision,
        CancellationToken cancellationToken = default) =>
        WriteAsync(async () =>
        {
            var entity = await LockAsync(id, expectedRevision, callerUserId, cancellationToken, allowAdministratorOnShared: asAdministrator);
            dbContext.AssetViews.Remove(entity);
        }, cancellationToken);

    public async Task<AssetViewRecord> UnshareAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Guid owner = Guid.Empty;
        await WriteAsync(async () =>
        {
            var entity = await LockAsync(id, expectedRevision: null, callerUserId: null, cancellationToken);
            owner = entity.OwnerUserId;
            if (entity.Visibility == Personal) return;
            entity.Visibility = Personal;
            entity.Revision++;
            entity.UpdatedAtUtc = Time.GetUtcNow();
        }, cancellationToken);
        return (await FindAsync(owner, id, cancellationToken))!;
    }

    private IQueryable<ViewRow> Readable(Guid viewerUserId) =>
        from view in dbContext.AssetViews.AsNoTracking()
        where view.OwnerUserId == viewerUserId || view.Visibility == Shared
        join user in dbContext.Users.AsNoTracking() on view.OwnerUserId equals user.Id into owners
        from owner in owners.DefaultIfEmpty()
        select new ViewRow(view, owner == null ? string.Empty : owner.DisplayName ?? owner.IdentityName, view.OwnerUserId);

    private async Task EnsureSharedRoomAsync(AssetViewVisibility visibility, Guid? excluding, CancellationToken cancellationToken)
    {
        if (visibility != AssetViewVisibility.Shared) return;
        if (await dbContext.AssetViews.CountAsync(item => item.Visibility == Shared && item.Id != excluding, cancellationToken) >=
            AssetViewDefinitionRules.MaximumSharedViews)
            throw new WellSiteAutoPilotException(AssetViewFailureCodes.SharedLimitReached, FailureKind.Conflict,
                "The limit of 100 shared views has been reached. Ask the owner of an unused shared view to remove it.");
    }

    /// <summary>
    /// Locks the row. With <paramref name="callerUserId"/> the caller must own it (or, as administrator, it must be
    /// Shared); without it the lock is for an administrator action chosen by the caller.
    /// </summary>
    private async Task<AssetViewEntity> LockAsync(Guid id, int? expectedRevision, Guid? callerUserId,
        CancellationToken cancellationToken, bool allowAdministratorOnShared = false)
    {
        var entity = (await dbContext.AssetViews.FromSqlInterpolated($"""
            SELECT v.* FROM assets.asset_views AS v WHERE v."Id" = {id} FOR UPDATE OF v
            """).ToArrayAsync(cancellationToken)).SingleOrDefault();
        if (entity is null || (callerUserId is { } caller && entity.OwnerUserId != caller &&
                !(allowAdministratorOnShared && entity.Visibility == Shared)))
            throw new WellSiteAutoPilotException(AssetViewFailureCodes.NotFound, FailureKind.NotFound, "The view was not found.");
        if (expectedRevision is { } expected && entity.Revision != expected)
            throw new WellSiteAutoPilotException(AssetViewFailureCodes.StaleRevision, FailureKind.Conflict,
                "The view was changed elsewhere. Reload it and try again.");
        return entity;
    }

    private async Task WriteAsync(Func<Task> stage, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            await stage();
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: NameIndex or SharedNameIndex } violation)
        {
            dbContext.ChangeTracker.Clear();
            throw violation.ConstraintName == SharedNameIndex
                ? new WellSiteAutoPilotException(AssetViewFailureCodes.SharedNameConflict, FailureKind.Conflict,
                    "A shared view with that name already exists. Choose another name to share it.", innerException: exception)
                : new WellSiteAutoPilotException(AssetViewFailureCodes.NameConflict, FailureKind.Conflict,
                    "You already have a view with that name.", innerException: exception);
        }
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();

    private sealed record ViewRow(AssetViewEntity View, string OwnerName, Guid OwnerUserId);

    private static AssetViewRecord ToRecord(ViewRow row) => new(row.View.Id, row.View.OwnerUserId, row.OwnerName,
        (AssetViewVisibility)row.View.Visibility, row.View.Name,
        JsonSerializer.Deserialize<AssetViewDefinition>(row.View.DefinitionJson)!, row.View.Revision, row.View.CreatedAtUtc,
        row.View.UpdatedAtUtc);
}
