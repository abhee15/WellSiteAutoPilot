using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Connections.Importing;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Connections;

public sealed class ConnectionImportRepository(WellSiteAutoPilotDbContext dbContext, TimeProvider? clock = null)
    : IConnectionImportRepository
{
    private const string EmptyAttributes = "{}";
    private TimeProvider Time => clock ?? TimeProvider.System;

    public async Task<IReadOnlySet<string>> BoundKeysAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var bound = await dbContext.SourceBindings.AsNoTracking().Where(item => item.ConnectionId == connectionId)
            .Select(item => new { item.ResourceKind, item.ExternalId }).ToArrayAsync(cancellationToken);
        return bound.Select(item => IConnectionImportRepository.BindingKey(item.ResourceKind, item.ExternalId))
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task<ImportApplyResult> ApplyAuthorizedAsync(ImportApplyRequest request, AccessSubject subject,
        SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.UserId != subject.UserId || string.IsNullOrWhiteSpace(actor.IdentityName) || actor.IdentityName.Length > 256)
            throw new WellSiteAutoPilotException("SECURITY_REQUEST_SUBJECT_INVALID", FailureKind.Authorization,
                "A bound authenticated actor is required.");
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await RequireAuthorityAsync(subject, cancellationToken);
            var connection = (await dbContext.Connections.FromSqlInterpolated($"""
                SELECT c.* FROM integration.connections AS c WHERE c."Id" = {request.ConnectionId} FOR UPDATE OF c
                """).AsNoTracking().ToArrayAsync(cancellationToken)).SingleOrDefault()
                ?? throw new WellSiteAutoPilotException(ConnectionFailureCodes.NotFound, FailureKind.NotFound,
                    "The connection was not found.");
            if (!connection.IsEnabled)
                throw new WellSiteAutoPilotException(ConnectionFailureCodes.Disabled, FailureKind.Conflict,
                    "Enable the connection before importing from it.");
            // The plan was made from the connection as it was then; if it has been edited since, it is not the plan reviewed.
            if (connection.Revision != request.ExpectedConnectionRevision)
                throw new WellSiteAutoPilotException(ConnectionFailureCodes.ImportPlanChanged, FailureKind.Conflict,
                    "The connection was changed since you reviewed this import. Review the import again.");

            // Hold the exact schemas being validated against, so a type cannot change or be hidden mid-import.
            var types = await dbContext.AssetTypes.FromSqlInterpolated($"""
                SELECT t.* FROM assets.asset_types AS t
                WHERE t."Id" IN ({request.WellTypeId}, {request.GroupTypeId}) AND t."IsActive" = TRUE FOR SHARE OF t
                """).AsNoTracking().ToDictionaryAsync(type => type.Id, cancellationToken);
            if (!types.TryGetValue(request.WellTypeId, out var wellType) || !types.TryGetValue(request.GroupTypeId, out var groupType))
                throw new WellSiteAutoPilotException("ASSET_TYPE_NOT_FOUND", FailureKind.NotFound,
                    "The asset types for this import are unavailable.");

            var attributeJson = request.Plan.Placements.ToDictionary(item => item.ExternalId,
                item => JsonSerializer.Serialize(item.Attributes), StringComparer.Ordinal);
            await ValidateAsync(wellType.AttributeSchemaJson, attributeJson.Values.Distinct(StringComparer.Ordinal), cancellationToken);
            var hasGroups = request.Plan.GroupCount > 0;
            if (hasGroups) await ValidateAsync(groupType.AttributeSchemaJson, [EmptyAttributes], cancellationToken);

            var now = Time.GetUtcNow();
            var existingGroups = hasGroups
                ? await LoadGroupsAsync(request.GroupTypeId, cancellationToken)
                : new Dictionary<(Guid? Parent, string Name), Guid>();
            var reused = new HashSet<Guid>();
            var groupsCreated = 0;
            var groupIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var placement in request.Plan.Placements)
            {
                Guid? parent = null;
                var key = string.Empty;
                foreach (var name in placement.GroupPath)
                {
                    key += "\u0001" + name;
                    if (!groupIds.TryGetValue(key, out var groupId))
                    {
                        if (existingGroups.TryGetValue((parent, name), out var existing))
                        {
                            groupId = existing;
                            reused.Add(existing);
                        }
                        else
                        {
                            groupId = Guid.NewGuid();
                            dbContext.Assets.Add(new AssetEntity
                            {
                                Id = groupId, AssetTypeId = request.GroupTypeId, Name = name, ParentAssetId = parent,
                                AttributeValuesJson = EmptyAttributes, IsActive = true, CreatedAtUtc = now
                            });
                            groupsCreated++;
                        }

                        groupIds[key] = groupId;
                    }

                    parent = groupId;
                }

                var assetId = Guid.NewGuid();
                dbContext.Assets.Add(new AssetEntity
                {
                    Id = assetId, AssetTypeId = request.WellTypeId, Name = placement.Name, ParentAssetId = parent,
                    AttributeValuesJson = attributeJson[placement.ExternalId], IsActive = true, CreatedAtUtc = now
                });
                dbContext.SourceBindings.Add(new SourceBindingEntity
                {
                    Id = Guid.NewGuid(), ConnectionId = request.ConnectionId, ResourceKind = placement.Kind,
                    ExternalId = placement.ExternalId, AssetId = assetId,
                    SnapshotJson = JsonSerializer.Serialize(placement.Snapshot), CreatedAtUtc = now
                });
            }

            var result = new ImportApplyResult(request.Plan.Placements.Count, groupsCreated, reused.Count);
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(), OccurredAtUtc = now, ActorUserId = subject.UserId,
                ActorIdentity = actor.IdentityName.Trim(), CorrelationId = actor.CorrelationId, Action = "connection.imported",
                TargetType = "connection", TargetId = request.ConnectionId.ToString(), AssetId = null,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    connection.AdapterKey, Levels = request.Plan.Levels, result.WellsCreated, result.GroupsCreated,
                    result.GroupsReused, request.Plan.Fingerprint
                })
            });
            // Authority is checked again after the long validation and before anything is written.
            await RequireAuthorityAsync(subject, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            throw new WellSiteAutoPilotException(ConnectionFailureCodes.ImportPlanChanged, FailureKind.Conflict,
                "Some of these objects were imported by someone else a moment ago. Review the import again.", innerException: exception);
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            dbContext.ChangeTracker.Clear();
            throw new WellSiteAutoPilotException("SECURITY_ACCESS_CONFLICT", FailureKind.Conflict,
                "Assets, access or the connection changed concurrently. Review the import again.", innerException: exception);
        }
    }

    private async Task RequireAuthorityAsync(AccessSubject subject, CancellationToken cancellationToken)
    {
        await GrantWriteGuard.RequireDeploymentAsync(dbContext, subject, WellSitePermissions.ConnectionsManage, Time, cancellationToken);
        await GrantWriteGuard.RequireAllAssetsAsync(dbContext, subject, WellSitePermissions.AssetsManage, Time, cancellationToken);
    }

    private static async Task ValidateAsync(string schemaJson, IEnumerable<string> values, CancellationToken cancellationToken)
    {
        foreach (var value in values) await AssetAttributeSchemaValidator.ValidateValuesAsync(schemaJson, value, cancellationToken);
    }

    // Groups are matched by parent and name only within the group type, as designed: never by name across the tree.
    private async Task<Dictionary<(Guid? Parent, string Name), Guid>> LoadGroupsAsync(Guid groupTypeId, CancellationToken cancellationToken)
    {
        var groups = await dbContext.Assets.AsNoTracking()
            .Where(asset => asset.AssetTypeId == groupTypeId && asset.IsActive)
            .OrderBy(asset => asset.CreatedAtUtc).ThenBy(asset => asset.Id)
            .Select(asset => new { asset.Id, asset.ParentAssetId, asset.Name }).ToArrayAsync(cancellationToken);
        var index = new Dictionary<(Guid? Parent, string Name), Guid>();
        foreach (var group in groups) index.TryAdd((group.ParentAssetId, group.Name), group.Id);
        return index;
    }
}
