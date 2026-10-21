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
    private TimeProvider Time => clock ?? TimeProvider.System;

    public async Task<IReadOnlyList<AssetViewRecord>> ListAsync(Guid ownerUserId, CancellationToken cancellationToken = default) =>
        (await dbContext.AssetViews.AsNoTracking().Where(item => item.OwnerUserId == ownerUserId)
            .OrderBy(item => item.NormalizedName).ToArrayAsync(cancellationToken)).Select(ToRecord).ToArray();

    public async Task<AssetViewRecord?> FindAsync(Guid ownerUserId, Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.AssetViews.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.OwnerUserId == ownerUserId, cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<AssetViewRecord> CreateAsync(Guid ownerUserId, string name, AssetViewDefinition definition,
        CancellationToken cancellationToken = default)
    {
        var now = Time.GetUtcNow();
        var id = Guid.NewGuid();
        await WriteAsync(async () =>
        {
            if (await dbContext.AssetViews.CountAsync(item => item.OwnerUserId == ownerUserId, cancellationToken) >=
                AssetViewDefinitionRules.MaximumViewsPerPerson)
                throw new WellSiteAutoPilotException(AssetViewFailureCodes.LimitReached, FailureKind.Conflict,
                    "You have reached the limit of 50 views. Delete one you no longer use first.");
            dbContext.AssetViews.Add(new AssetViewEntity
            {
                Id = id, OwnerUserId = ownerUserId, Name = name, NormalizedName = Normalize(name),
                DefinitionJson = JsonSerializer.Serialize(definition), Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            });
        }, cancellationToken);
        return (await FindAsync(ownerUserId, id, cancellationToken))!;
    }

    public async Task<AssetViewRecord> UpdateAsync(Guid ownerUserId, Guid id, int expectedRevision, string name,
        AssetViewDefinition definition, CancellationToken cancellationToken = default)
    {
        await WriteAsync(async () =>
        {
            var entity = await LockAsync(ownerUserId, id, expectedRevision, cancellationToken);
            entity.Name = name;
            entity.NormalizedName = Normalize(name);
            entity.DefinitionJson = JsonSerializer.Serialize(definition);
            entity.Revision++;
            entity.UpdatedAtUtc = Time.GetUtcNow();
        }, cancellationToken);
        return (await FindAsync(ownerUserId, id, cancellationToken))!;
    }

    public Task DeleteAsync(Guid ownerUserId, Guid id, int expectedRevision, CancellationToken cancellationToken = default) =>
        WriteAsync(async () =>
            dbContext.AssetViews.Remove(await LockAsync(ownerUserId, id, expectedRevision, cancellationToken)), cancellationToken);

    private async Task<AssetViewEntity> LockAsync(Guid ownerUserId, Guid id, int expectedRevision, CancellationToken cancellationToken)
    {
        var entity = (await dbContext.AssetViews.FromSqlInterpolated($"""
            SELECT v.* FROM assets.asset_views AS v WHERE v."Id" = {id} AND v."OwnerUserId" = {ownerUserId} FOR UPDATE OF v
            """).ToArrayAsync(cancellationToken)).SingleOrDefault()
            ?? throw new WellSiteAutoPilotException(AssetViewFailureCodes.NotFound, FailureKind.NotFound, "The view was not found.");
        if (entity.Revision != expectedRevision)
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
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: NameIndex })
        {
            dbContext.ChangeTracker.Clear();
            throw new WellSiteAutoPilotException(AssetViewFailureCodes.NameConflict, FailureKind.Conflict,
                "You already have a view with that name.", innerException: exception);
        }
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();

    private static AssetViewRecord ToRecord(AssetViewEntity entity) => new(entity.Id, entity.OwnerUserId, entity.Name,
        JsonSerializer.Deserialize<AssetViewDefinition>(entity.DefinitionJson)!, entity.Revision, entity.CreatedAtUtc,
        entity.UpdatedAtUtc);
}
