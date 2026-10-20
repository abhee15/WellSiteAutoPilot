using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Assets;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Assets;

public sealed partial class AssetRepository
{
    public Task AddAssetTypeAuthorizedAsync(AssetTypeDefinition assetType, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        if (assetType.Id == Guid.Empty || assetType.SchemaVersion != 1 || !assetType.IsActive ||
            !ValidText(assetType.Key, 128) || !ValidText(assetType.DisplayName, 256)) throw InvalidMetadata();
        return WriteAuthorizedAsync(subject, actor, async () =>
        {
            var time = clock ?? TimeProvider.System;
            // The type catalog is deployment-wide, so its authority is a deployment-wide permission.
            await GrantWriteGuard.RequireDeploymentAsync(dbContext, subject, WellSitePermissions.AssetTypesManage, time, cancellationToken);
            await AssetAttributeSchemaValidator.ValidateDefinitionAsync(assetType.AttributeSchemaJson, cancellationToken);
            await GrantWriteGuard.RequireDeploymentAsync(dbContext, subject, WellSitePermissions.AssetTypesManage, time, cancellationToken);
            StageAssetType(assetType);
            StageCreationAudit("asset-type.created", "asset-type", assetType.Id, null, subject, actor,
                new { assetType.Key, assetType.SchemaVersion });
        }, cancellationToken);
    }

    public Task AddAssetAuthorizedAsync(Asset asset, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Id == Guid.Empty || asset.AssetTypeId == Guid.Empty || !asset.IsActive ||
            asset.ParentAssetId == Guid.Empty || asset.ParentAssetId == asset.Id || !ValidText(asset.Name, 256)) throw InvalidMetadata();
        return WriteAuthorizedAsync(subject, actor, async () =>
        {
            var time = clock ?? TimeProvider.System;
            // Hold the exact schema being used for authoritative validation.
            // An application precheck alone cannot fence schema retirement/change.
            var types = await dbContext.AssetTypes.FromSqlInterpolated($"""
                SELECT t.* FROM assets.asset_types AS t WHERE t."Id" = {asset.AssetTypeId} AND t."IsActive" = TRUE
                FOR SHARE OF t
                """).AsNoTracking().ToArrayAsync(cancellationToken);
            if (types.Length != 1 || types[0].SchemaVersion < 1)
                throw new WellSiteAutoPilotException("ASSET_TYPE_NOT_FOUND", FailureKind.NotFound, "The selected Asset Type is unavailable.");
            await RequireCreationScopeAsync();
            await AssetAttributeSchemaValidator.ValidateValuesAsync(types[0].AttributeSchemaJson, asset.AttributeValuesJson, cancellationToken);
            await RequireCreationScopeAsync();
            StageAsset(asset);
            StageCreationAudit("asset.created", "asset", asset.Id, asset.Id, subject, actor,
                new { asset.AssetTypeId, asset.ParentAssetId, SchemaVersion = types[0].SchemaVersion });

            async Task RequireCreationScopeAsync()
            {
                if (asset.ParentAssetId is { } parent)
                    await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.AssetsManage, [parent], time, cancellationToken);
                else
                {
                    // A root affects the whole hierarchy, so it needs authority over every asset.
                    await GrantWriteGuard.RequireAllAssetsAsync(dbContext, subject, WellSitePermissions.AssetsManage, time, cancellationToken);
                }
            }
        }, cancellationToken);
    }

    private async Task WriteAuthorizedAsync(AccessSubject subject, SecurityActorContext actor, Func<Task> stage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.UserId != subject.UserId || !ValidText(actor.IdentityName, 256))
            throw new WellSiteAutoPilotException("SECURITY_REQUEST_SUBJECT_INVALID", FailureKind.Authorization,
                "A bound authenticated actor is required.");
        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await stage();
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_asset_types_Key" })
        {
            throw new WellSiteAutoPilotException("ASSET_TYPE_KEY_CONFLICT", FailureKind.Conflict,
                "An Asset Type with the same key already exists.", innerException: exception);
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException postgres &&
            postgres.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            throw new WellSiteAutoPilotException("SECURITY_ACCESS_CONFLICT", FailureKind.Conflict,
                "Asset dependencies or access changed concurrently. Reload before retrying.", innerException: exception);
        }
    }

    private void StageCreationAudit(string action, string targetType, Guid targetId, Guid? assetId,
        AccessSubject subject, SecurityActorContext actor, object details) => dbContext.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(), OccurredAtUtc = (clock ?? TimeProvider.System).GetUtcNow(), ActorUserId = subject.UserId,
            ActorIdentity = actor.IdentityName.Trim(), CorrelationId = actor.CorrelationId, Action = action,
            TargetType = targetType, TargetId = targetId.ToString(), AssetId = assetId, DetailsJson = JsonSerializer.Serialize(details)
        });

    private static bool ValidText(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && !value.Any(char.IsControl);
    private static WellSiteAutoPilotException InvalidMetadata() => new(FailureCodes.ValidationFailed, FailureKind.Validation,
        "Asset creation metadata is invalid.");
}
