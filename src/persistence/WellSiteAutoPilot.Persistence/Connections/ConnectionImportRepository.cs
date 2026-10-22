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

    public async Task<ImportHierarchyStatus> ReadHierarchyStatusAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var saved = await dbContext.ImportHierarchies.AsNoTracking().SingleOrDefaultAsync(item => item.ConnectionId == connectionId, cancellationToken);
        return saved is not null ? new("Saved", JsonSerializer.Deserialize<string[]>(saved.LevelsJson))
            : new(await dbContext.SourceBindings.AnyAsync(binding => binding.ConnectionId == connectionId, cancellationToken) ? "Legacy" : "New", null);
    }

    public async Task<ImportHierarchyState> ReadHierarchyAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var saved = await dbContext.ImportHierarchies.AsNoTracking().SingleOrDefaultAsync(item => item.ConnectionId == connectionId, cancellationToken);
        if (saved is not null)
        {
            var stableGroups = await dbContext.ImportedGroups.AsNoTracking().Where(item => item.ConnectionId == connectionId)
                .Join(dbContext.Assets, binding => binding.AssetId, asset => asset.Id, (binding, asset) => new ImportedStableGroup(
                    binding.GroupKey, asset.Id, asset.ParentAssetId, asset.Name, binding.SourceLabel, asset.IsActive, asset.AssetTypeId, asset.OwnerConnectionId))
                .ToArrayAsync(cancellationToken);
            stableGroups = stableGroups.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
            if (stableGroups.Any(group => group.OwnerConnectionId != connectionId)) throw PlanChanged();
            return new(JsonSerializer.Deserialize<string[]>(saved.LevelsJson) ?? throw new InvalidOperationException("Invalid saved hierarchy."), [], [], saved.Revision, JsonSerializer.Deserialize<ImportGroupingField[]>(saved.FieldsJson), stableGroups);
        }
        if (await dbContext.SourceBindings.AnyAsync(binding => binding.ConnectionId == connectionId &&
            !dbContext.Assets.Any(asset => asset.Id == binding.AssetId && asset.OwnerConnectionId == connectionId), cancellationToken)) throw PlanChanged();
        var wells = await dbContext.SourceBindings.AsNoTracking().Where(item => item.ConnectionId == connectionId)
            .Join(dbContext.Assets, binding => binding.AssetId, asset => asset.Id, (binding, asset) => new LegacyImportWell(
                asset.Id, binding.ResourceKind, binding.ExternalId, asset.ParentAssetId, binding.SnapshotJson))
            .ToArrayAsync(cancellationToken);
        wells = wells.OrderBy(item => item.AssetId).ToArray();
        if (wells.Length == 0) return new(null, [], []);
        var groups = await dbContext.Assets.AsNoTracking().Where(asset => asset.OwnerConnectionId == connectionId &&
                dbContext.AssetTypes.Any(type => type.Id == asset.AssetTypeId && type.Key == ConnectionImportService.GroupTypeKey))
            .OrderBy(asset => asset.Id).Select(asset => new LegacyImportGroup(asset.Id, asset.ParentAssetId, asset.Name, asset.IsActive))
            .ToArrayAsync(cancellationToken);
        return new(null, wells, groups);
    }

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

            var hierarchy = await ReadHierarchyAsync(request.ConnectionId, cancellationToken);
            if (hierarchy.Fingerprint != request.ExpectedHierarchyFingerprint ||
                (hierarchy.Levels is { } savedLevels && (!savedLevels.SequenceEqual(request.Plan.Levels) || !hierarchy.MatchesFields(request.Plan.Levels, request.Fields))))
                throw PlanChanged();
            var adopting = hierarchy.Status.Status == "Legacy";
            if (adopting && !request.AdoptExistingHierarchy) throw PlanChanged();
            var adoption = adopting ? hierarchy.Adoption(request.ConnectionId, request.Plan.Levels, request.Fields) : [];
            var now = Time.GetUtcNow();
            var legacyGroups = hierarchy.Groups.ToDictionary(group => group.AssetId);
            if (hierarchy.Levels is null)
            {
                dbContext.ImportHierarchies.Add(new ImportHierarchyEntity { ConnectionId = request.ConnectionId,
                    LevelsJson = JsonSerializer.Serialize(request.Plan.Levels), FieldsJson = JsonSerializer.Serialize(request.Plan.Levels.Select(key =>
                        new ImportGroupingField(key, request.Fields.Single(field => field.Key == key).IdentityMetadataKey))), CreatedAtUtc = now });
                foreach (var group in adoption) dbContext.ImportedGroups.Add(new ImportedGroupEntity {
                    ConnectionId = request.ConnectionId, GroupKey = group.Key, AssetId = group.AssetId,
                    SourceLabel = legacyGroups[group.AssetId].Name });
            }

            // Hold the exact schemas being validated against, so a type cannot change or be hidden mid-import.
            var types = await dbContext.AssetTypes.FromSqlInterpolated($"""
                SELECT t.* FROM assets.asset_types AS t
                WHERE t."Id" IN ({request.WellTypeId}, {request.GroupTypeId}) AND t."IsActive" = TRUE FOR SHARE OF t
                """).AsNoTracking().ToDictionaryAsync(type => type.Id, cancellationToken);
            if (!types.TryGetValue(request.WellTypeId, out var wellType) || !types.TryGetValue(request.GroupTypeId, out var groupType))
                throw new WellSiteAutoPilotException("ASSET_TYPE_NOT_FOUND", FailureKind.NotFound,
                    "The asset types for this import are unavailable.");

            var attributeJson = request.Plan.Placements.ToDictionary(item => (item.Kind, item.ExternalId),
                item => JsonSerializer.Serialize(item.Attributes));
            await ValidateAsync(wellType.AttributeSchemaJson, attributeJson.Values.Distinct(StringComparer.Ordinal), cancellationToken);
            var hasGroups = request.Plan.GroupCount > 0;
            if (hasGroups) await ValidateAsync(groupType.AttributeSchemaJson, [EmptyAttributes], cancellationToken);

            var existingGroups = hasGroups
                ? (hierarchy.StableGroups ?? []).ToDictionary(group => group.Key,
                    group => (Id: group.AssetId, Parent: group.ParentId, Active: group.IsActive, TypeId: group.TypeId), StringComparer.Ordinal)
                : new Dictionary<string, (Guid Id, Guid? Parent, bool Active, Guid TypeId)>(StringComparer.Ordinal);
            foreach (var group in adoption)
            {
                var asset = legacyGroups[group.AssetId];
                existingGroups[group.Key] = (asset.AssetId, asset.ParentId, asset.IsActive, request.GroupTypeId);
            }
            var reused = new HashSet<Guid>();
            var groupsCreated = 0;
            var groupIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var placement in request.Plan.Placements)
            {
                Guid? parent = null;
                for (var depth = 0; depth < placement.GroupPath.Count; depth++)
                {
                    var name = placement.GroupPath[depth];
                    var key = placement.GroupKeys[depth];
                    if (!groupIds.TryGetValue(key, out var groupId))
                    {
                        if (existingGroups.TryGetValue(key, out var existing))
                        {
                            if (existing.Parent != parent || !existing.Active || existing.TypeId != request.GroupTypeId) throw PlanChanged();
                            groupId = existing.Id;
                            reused.Add(existing.Id);
                        }
                        else
                        {
                            groupId = Guid.NewGuid();
                            dbContext.Assets.Add(new AssetEntity
                            {
                                Id = groupId, AssetTypeId = request.GroupTypeId, Name = name, ParentAssetId = parent,
                                AttributeValuesJson = EmptyAttributes, IsActive = true, CreatedAtUtc = now,
                                OwnerConnectionId = request.ConnectionId
                            });
                            dbContext.ImportedGroups.Add(new ImportedGroupEntity { ConnectionId = request.ConnectionId, GroupKey = key, AssetId = groupId, SourceLabel = name });
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
                    AttributeValuesJson = attributeJson[(placement.Kind, placement.ExternalId)], IsActive = true, CreatedAtUtc = now,
                    OwnerConnectionId = request.ConnectionId
                });
                dbContext.SourceBindings.Add(new SourceBindingEntity
                {
                    Id = Guid.NewGuid(), ConnectionId = request.ConnectionId, ResourceKind = placement.Kind,
                    ExternalId = placement.ExternalId, AssetId = assetId,
                    SnapshotJson = ImportedSourceBaseline.Capture(placement, request.Plan.Levels, request.UnavailableFields, request.ActingAs), CreatedAtUtc = now
                });
            }

            var result = new ImportApplyResult(request.Plan.Placements.Count, groupsCreated, reused.Count, adopting);
            dbContext.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(), OccurredAtUtc = now, ActorUserId = subject.UserId,
                ActorIdentity = actor.IdentityName.Trim(), CorrelationId = actor.CorrelationId, Action = "connection.imported",
                TargetType = "connection", TargetId = request.ConnectionId.ToString(), AssetId = null,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    connection.AdapterKey, Levels = request.Plan.Levels, result.WellsCreated, result.GroupsCreated,
                    result.GroupsReused, result.HierarchyAdopted, request.Plan.Fingerprint
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

    private static WellSiteAutoPilotException PlanChanged() => new(ConnectionFailureCodes.ImportPlanChanged, FailureKind.Conflict,
        "The imported hierarchy changed or needs a reviewed adoption. Review the import again.");
}
