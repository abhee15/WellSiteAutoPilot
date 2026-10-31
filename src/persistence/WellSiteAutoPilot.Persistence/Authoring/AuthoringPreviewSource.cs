using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Authoring;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Authoring;

/// <summary>
/// Preview reads reuse the Shadow read-plan rules (imported asset, enabled connection, single source binding,
/// supported quantity, asset grants) without persisting an execution or plan.
/// </summary>
public sealed class AuthoringPreviewSource(WellSiteAutoPilotDbContext dbContext, TimeProvider? clock = null) : IAuthoringPreviewSource
{
    public async Task<AuthorizedExecutionRead> ResolveAsync(AccessSubject subject, Guid assetId, string quantity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var time = clock ?? TimeProvider.System;
        await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ConfiguredLogicRead,
            [assetId], time, cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var asset = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == assetId, cancellationToken);
        if (asset is null || !asset.IsActive || asset.OwnerConnectionId is not { } connectionId)
            throw Invalid("AUTHORING_PREVIEW_SOURCE_INVALID", "The asset is not an active imported asset with a connection.");
        var connection = await dbContext.Connections.FromSqlInterpolated($"""
            SELECT c.* FROM integration.connections AS c WHERE c."Id" = {connectionId} FOR SHARE OF c
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (connection is null || !connection.IsEnabled)
            throw Invalid("AUTHORING_PREVIEW_CONNECTION_DISABLED", "The asset's connection is disabled.");
        if (AdapterReadRules.Find(connection.AdapterKey) is not { } rule)
            throw Invalid("AUTHORING_PREVIEW_PROVIDER_UNSUPPORTED", "The asset's provider cannot serve direct current reads.");
        if (!rule.CurrentQuantities.Contains(quantity))
            throw Invalid("AUTHORING_PREVIEW_QUANTITY_UNSUPPORTED", "The provider cannot serve that quantity.");
        var bindings = await dbContext.SourceBindings.FromSqlInterpolated($"""
            SELECT b.* FROM integration.source_bindings AS b WHERE b."AssetId" = {assetId}
            ORDER BY b."Id" FOR SHARE OF b
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (bindings.Length != 1 || bindings[0].ConnectionId != connectionId || bindings[0].ResourceKind != rule.ResourceKind)
            throw Invalid("AUTHORING_PREVIEW_SOURCE_INVALID", "The asset does not have exactly one valid source binding.");
        var settings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(connection.SettingsJson,
            System.Text.Json.JsonSerializerOptions.Web) ?? [];
        await transaction.CommitAsync(cancellationToken);
        return new(connection.AdapterKey, connection.Id, connection.Revision, bindings[0].ExternalId, quantity, settings);
    }

    private static WellSiteAutoPilotException Invalid(string code, string message) =>
        new(code, FailureKind.Validation, message);
}
