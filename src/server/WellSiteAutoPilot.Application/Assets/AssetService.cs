using System.Text.Json;
using WellSiteAutoPilot.Domain.Assets;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Assets;

public sealed class AssetService(
    IAssetRepository repository,
    TimeProvider timeProvider)
{
    public async Task<AssetTypeDefinition> CreateAssetTypeAsync(
        CreateAssetTypeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var key = RequireText(command.Key, "Asset type key is required.");
        var displayName = RequireText(command.DisplayName, "Asset type display name is required.");
        ValidateJson(command.AttributeSchemaJson, "Asset type attribute schema must be valid JSON.");

        if (await repository.AssetTypeKeyExistsAsync(key, cancellationToken))
        {
            throw new WellSiteAutoPilotException(
                "ASSET_TYPE_KEY_CONFLICT",
                FailureKind.Conflict,
                "An asset type with the same key already exists.");
        }

        var assetType = new AssetTypeDefinition(
            Guid.NewGuid(),
            key,
            displayName,
            1,
            command.AttributeSchemaJson,
            true,
            timeProvider.GetUtcNow());

        await repository.AddAssetTypeAsync(assetType, cancellationToken);
        return assetType;
    }

    public async Task<Asset> CreateAssetAsync(
        CreateAssetCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.AssetTypeId == Guid.Empty)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Asset type is required.");
        }

        var name = RequireText(command.Name, "Asset name is required.");
        ValidateJson(command.AttributeValuesJson, "Asset attributes must be valid JSON.");

        var assetType = await repository.GetAssetTypeAsync(command.AssetTypeId, cancellationToken);
        if (assetType is null || !assetType.IsActive)
        {
            throw new WellSiteAutoPilotException(
                "ASSET_TYPE_NOT_FOUND",
                FailureKind.NotFound,
                "The selected asset type was not found.");
        }

        if (command.ParentAssetId is not null)
        {
            var parent = await repository.GetAssetAsync(command.ParentAssetId.Value, cancellationToken);
            if (parent is null || !parent.IsActive)
            {
                throw new WellSiteAutoPilotException(
                    "PARENT_ASSET_NOT_FOUND",
                    FailureKind.NotFound,
                    "The selected parent asset was not found.");
            }
        }

        var asset = new Asset(
            Guid.NewGuid(),
            command.AssetTypeId,
            name,
            command.ParentAssetId,
            command.AttributeValuesJson,
            true,
            timeProvider.GetUtcNow());

        await repository.AddAssetAsync(asset, cancellationToken);
        return asset;
    }

    public async Task<AssetTypeDefinition> GetRequiredAssetTypeAsync(
        Guid assetTypeId,
        CancellationToken cancellationToken = default) =>
        await repository.GetAssetTypeAsync(assetTypeId, cancellationToken) ??
        throw new WellSiteAutoPilotException(
            "ASSET_TYPE_NOT_FOUND",
            FailureKind.NotFound,
            "The requested asset type was not found.");

    public Task<IReadOnlyCollection<AssetTypeDefinition>> ListAssetTypesAsync(
        CancellationToken cancellationToken = default) =>
        repository.ListAssetTypesAsync(cancellationToken);

    public async Task<Asset> GetRequiredAssetAsync(
        Guid assetId,
        CancellationToken cancellationToken = default) =>
        await repository.GetAssetAsync(assetId, cancellationToken) ??
        throw new WellSiteAutoPilotException(
            "ASSET_NOT_FOUND",
            FailureKind.NotFound,
            "The requested asset was not found.");

    public Task<IReadOnlyCollection<Asset>> ListAssetsAsync(
        Guid? assetTypeId,
        Guid? parentAssetId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Asset list limit must be between 1 and 500.");
        }

        return repository.ListAssetsAsync(assetTypeId, parentAssetId, limit, cancellationToken);
    }

    public Task<IReadOnlyCollection<Asset>> ListAssetsInScopeAsync(
        Guid? assetTypeId,
        Guid? parentAssetId,
        int limit,
        IReadOnlyCollection<Guid> allowedAssetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedAssetIds);

        if (limit is < 1 or > 500)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                "Asset list limit must be between 1 and 500.");
        }

        return repository.ListAssetsInScopeAsync(
            assetTypeId,
            parentAssetId,
            limit,
            allowedAssetIds,
            cancellationToken);
    }

    private static string RequireText(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                message);
        }

        return value.Trim();
    }

    private static void ValidateJson(string json, string message)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                message);
        }

        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                message,
                innerException: exception);
        }
    }
}
