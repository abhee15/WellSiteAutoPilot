using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Connections;

/// <summary>Inspects an explicitly pinned source context without calling a provider or acquiring a value.</summary>
public sealed class AssetOperationService(IConnectionRepository connections, IGatewayAdapters gateway,
    IAssetConnectionSourceReader assets, IRequestAccessRepository access)
{
    public async Task<AssetOperationsSnapshot> InspectAsync(AccessSubject subject, Guid connectionId, Guid assetId,
        string adapterKey, int expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (connectionId == Guid.Empty || assetId == Guid.Empty || string.IsNullOrWhiteSpace(adapterKey) || adapterKey.Length > 64 || expectedRevision < 1)
            throw new WellSiteAutoPilotException("OPERATION_CONTEXT_INVALID", FailureKind.Validation, "Specify the Connection, Asset, adapter and current Connection revision.");
        await RequireConnectionAccessAsync();
        var source = await assets.GetConnectionSourceAuthorizedAsync(subject, assetId, connectionId, cancellationToken) ?? throw NotFound();
        if (source.AssetId != assetId || source.ConnectionId != connectionId) throw NotFound();
        var connection = await connections.FindAsync(connectionId, cancellationToken) ?? throw NotFound();
        if (connection.AdapterKey != adapterKey)
            throw new WellSiteAutoPilotException("OPERATION_ADAPTER_MISMATCH", FailureKind.Validation, "The selected adapter does not match the saved Connection.");
        if (connection.Revision != expectedRevision) throw ContextChanged();
        var descriptor = (await gateway.ListAsync(cancellationToken)).SingleOrDefault(adapter => adapter.Key == adapterKey);
        // Gateway metadata retrieval is I/O. Recheck grants, ownership/bindings and revision after it returns.
        await RequireConnectionAccessAsync();
        var currentSource = await assets.GetConnectionSourceAuthorizedAsync(subject, assetId, connectionId, cancellationToken) ?? throw NotFound();
        if (currentSource.AssetId != assetId || currentSource.ConnectionId != connectionId) throw NotFound();
        var current = await connections.FindAsync(connectionId, cancellationToken) ?? throw NotFound();
        if (current.Revision != expectedRevision || current.AdapterKey != adapterKey || current.SettingsVersion != connection.SettingsVersion ||
            currentSource.IsActive != source.IsActive || !currentSource.Bindings.SequenceEqual(source.Bindings)) throw ContextChanged();
        return new(connectionId, current.Revision, assetId, adapterKey,
            ConnectionOperationAvailability.Evaluate(current, descriptor).State,
            AssetOperationEligibilityEvaluator.Evaluate(current, descriptor, currentSource));

        async Task RequireConnectionAccessAsync()
        {
            if (!await access.HasDeploymentPermissionAsync(subject, WellSitePermissions.ConnectionsRead, cancellationToken))
                throw new WellSiteAutoPilotException("OPERATION_METADATA_FORBIDDEN", FailureKind.Authorization, "Connection metadata access is required.");
        }
    }

    private static WellSiteAutoPilotException NotFound() => new("CONNECTION_ASSET_NOT_FOUND", FailureKind.NotFound,
        "The Asset was not found in the selected Connection or is not accessible.");
    private static WellSiteAutoPilotException ContextChanged() => new("OPERATION_CONTEXT_CHANGED", FailureKind.Conflict,
        "The Connection or imported source changed. Refresh the metadata and retry with the current revision.");
}
