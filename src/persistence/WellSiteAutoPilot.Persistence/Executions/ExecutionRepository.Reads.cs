using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Persistence.Security;

namespace WellSiteAutoPilot.Persistence.Executions;

public sealed partial class ExecutionRepository
{
    private async Task<ExecutionInputBindingV2[]> PinReadSourcesAsync(ConfiguredShadowExecutionCommand command,
        AccessSubject? subject, CancellationToken ct)
    {
        var result = new List<ExecutionInputBindingV2>();
        foreach (var input in command.Inputs)
        {
            ExecutionReadSourceV3? source = null;
            // Simulator remains an explicit legacy path, never a fallback for imported Assets.
            var asset = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == input.AssetId, ct);
            var simulatorOwned = asset?.OwnerConnectionId is { } ownerId && await dbContext.Connections.AsNoTracking()
                .AnyAsync(c => c.Id == ownerId && c.AdapterKey == "simulator", ct);
            if (string.Equals(input.ProviderId, "foresite", StringComparison.OrdinalIgnoreCase) ||
                asset?.OwnerConnectionId is not null && !simulatorOwned)
            {
                if (subject is null) throw ReadFailure("EXECUTION_READ_AUTHORITY_REQUIRED");
                if (asset is null || !asset.IsActive || asset.OwnerConnectionId is not { } connectionId)
                    throw ReadFailure("EXECUTION_READ_SOURCE_INVALID");
                var connection = await dbContext.Connections.FromSqlInterpolated($"""
                    SELECT c.* FROM integration.connections AS c WHERE c."Id" = {connectionId} FOR SHARE OF c
                    """).AsNoTracking().SingleOrDefaultAsync(ct);
                if (connection is null || !connection.IsEnabled) throw ReadFailure("EXECUTION_CONNECTION_DISABLED");
                if (connection.AdapterKey != "foresite" || !string.Equals(input.ProviderId, connection.AdapterKey, StringComparison.OrdinalIgnoreCase))
                    throw ReadFailure("EXECUTION_READ_PROVIDER_UNSUPPORTED");
                var bindings = await dbContext.SourceBindings.FromSqlInterpolated($"""
                    SELECT b.* FROM integration.source_bindings AS b WHERE b."AssetId" = {input.AssetId}
                    ORDER BY b."Id" FOR SHARE OF b
                    """).AsNoTracking().ToArrayAsync(ct);
                if (bindings.Length != 1 || bindings[0].ConnectionId != connectionId ||
                    bindings[0].ResourceKind != "foresite.well" || bindings[0].ExternalId != input.ProviderAssetExternalId)
                    throw ReadFailure("EXECUTION_READ_SOURCE_INVALID");
                if (input.Access != "Current" ||
                    !(input.Quantity is "PumpFillage" or "AveragePumpFillage" && input.RequiredCapability is null && input.SchemaId is null ||
                      input.Quantity == "SurfaceCards" && input.RequiredCapability == "structured.current" &&
                      input.SchemaId == "foresite.surface-cards/v1"))
                    throw ReadFailure("EXECUTION_READ_QUANTITY_UNSUPPORTED");
                using var mapping = JsonDocument.Parse(input.ProviderMappingJson);
                if (mapping.RootElement.ValueKind != JsonValueKind.Object || mapping.RootElement.EnumerateObject().Any())
                    throw ReadFailure("EXECUTION_READ_MAPPING_UNSUPPORTED");
                await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ExecutionsRequest,
                    [input.AssetId], clock ?? TimeProvider.System, ct);
                await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ConfiguredLogicRead,
                    [input.AssetId], clock ?? TimeProvider.System, ct);
                source = new(Guid.NewGuid(), connectionId, connection.Revision, bindings[0].Id, bindings[0].ResourceKind);
            }
            result.Add(new(input.RequirementId, input.AssetId, input.ProviderId, input.ProviderAssetExternalId,
                input.Quantity, input.Access, input.CanonicalUnit, input.MaximumAgeSeconds, input.AllowUncertainQuality,
                input.ProviderMappingJson, source, input.RequiredCapability, input.SchemaId));
        }
        return result.ToArray();
    }

    public async Task<AuthorizedExecutionRead> ResolveReadAsync(Guid executionId, Guid readPlanId, CancellationToken cancellationToken = default)
    {
        if (executionId == Guid.Empty || readPlanId == Guid.Empty) throw ReadFailure("EXECUTION_READ_PLAN_INVALID");
        // No Gateway-local plan cache: restart, replay and multiple Gateways all consult this durable snapshot.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var execution = await dbContext.Executions.FromSqlInterpolated($"""
            SELECT e.* FROM operations.executions AS e WHERE e."Id" = {executionId} FOR SHARE OF e
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        if (execution is null || execution.RequestContractVersion != 3 || execution.Mode != "Shadow" ||
            !ActiveExecutionStatuses.Contains(execution.Status) || execution.RequestPayloadJson is null || execution.ReadAuthorityJson is null)
            throw ReadFailure("EXECUTION_READ_PLAN_INVALID");
        var request = JsonSerializer.Deserialize<ExecutionRequestedV3>(execution.RequestPayloadJson, SerializerOptions)
            ?? throw ReadFailure("EXECUTION_READ_PLAN_INVALID");
        var inputs = request.Execution.Inputs.Where(i => i.Source?.ReadPlanId == readPlanId).ToArray();
        if (inputs.Length != 1) throw ReadFailure("EXECUTION_READ_PLAN_INVALID");
        var input = inputs[0];
        var source = input.Source!;
        var authority = JsonSerializer.Deserialize<ReadAuthority>(execution.ReadAuthorityJson, SerializerOptions)
            ?? throw ReadFailure("EXECUTION_READ_AUTHORITY_REQUIRED");
        if (now < authority.CreatedAtUtc || now >= authority.ExpiresAtUtc) throw ReadFailure("EXECUTION_READ_PLAN_EXPIRED");
        var subject = authority.ToSubject();
        var targets = await dbContext.ExecutionAssetScopes.AsNoTracking().Where(s => s.ExecutionId == executionId)
            .Select(s => s.AssetId).ToArrayAsync(cancellationToken);
        if (targets.Length == 0 || !targets.Contains(input.AssetId)) throw ReadFailure("EXECUTION_READ_PLAN_INVALID");
        await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ExecutionsRequest,
            targets, clock ?? TimeProvider.System, cancellationToken);
        await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ConfiguredLogicRead,
            targets, clock ?? TimeProvider.System, cancellationToken);
        var configurations = await dbContext.ConfiguredLogicDefinitions.FromSqlInterpolated($"""
            SELECT c.* FROM logic.configured_logic AS c WHERE c."Id" = {execution.LogicInstanceId} FOR SHARE OF c
            """).AsNoTracking().ToArrayAsync(cancellationToken);
        if (configurations.Length != 1 || configurations[0].ActiveRevisionId != execution.ConfigurationRevisionId) throw ReadFailure("EXECUTION_READ_CONFIGURATION_CHANGED");
        var asset = await dbContext.Assets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == input.AssetId, cancellationToken);
        var binding = await dbContext.SourceBindings.FromSqlInterpolated($"""
            SELECT b.* FROM integration.source_bindings AS b WHERE b."Id" = {source.SourceBindingId} FOR SHARE OF b
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var connection = await dbContext.Connections.FromSqlInterpolated($"""
            SELECT c.* FROM integration.connections AS c WHERE c."Id" = {source.ConnectionId} FOR SHARE OF c
            """).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (asset is null || !asset.IsActive || asset.OwnerConnectionId != source.ConnectionId || binding is null ||
            binding.AssetId != input.AssetId || binding.ConnectionId != source.ConnectionId ||
            binding.ResourceKind != source.ResourceKind || binding.ExternalId != input.ProviderAssetExternalId)
            throw ReadFailure("EXECUTION_READ_SOURCE_CHANGED");
        if (connection is null || !connection.IsEnabled) throw ReadFailure("EXECUTION_CONNECTION_DISABLED");
        if (connection.Revision != source.ConnectionRevision || connection.AdapterKey != input.ProviderId)
            throw ReadFailure("EXECUTION_READ_CONNECTION_CHANGED");
        var settings = JsonSerializer.Deserialize<Dictionary<string, string?>>(connection.SettingsJson, SerializerOptions)
            ?? throw ReadFailure("EXECUTION_READ_CONNECTION_INVALID");
        now = (clock ?? TimeProvider.System).GetUtcNow();
        if (now >= authority.ExpiresAtUtc) throw ReadFailure("EXECUTION_READ_PLAN_EXPIRED");
        await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ExecutionsRequest,
            targets, clock ?? TimeProvider.System, cancellationToken);
        await GrantWriteGuard.RequireAssetsAsync(dbContext, subject, WellSitePermissions.ConfiguredLogicRead,
            targets, clock ?? TimeProvider.System, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(connection.AdapterKey, connection.Id, connection.Revision, binding.ExternalId, input.Quantity, settings);
    }

    // Persisted only on the execution row, never in transport/public projections or module input.
    private sealed record ReadAuthority(Guid UserId, string[]? GroupSids, DateTimeOffset? ObservedAtUtc,
        DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc)
    {
        public static ReadAuthority Create(AccessSubject subject, DateTimeOffset now) => new(subject.UserId,
            subject.WindowsGroups?.GroupSids.ToArray(), subject.WindowsGroups?.ObservedAtUtc, now,
            subject.WindowsGroups is { } groups ? groups.ObservedAtUtc + VerifiedWindowsGroupMembership.MaximumAge : now.AddMinutes(5));
        public AccessSubject ToSubject() => new(UserId, GroupSids is not null && ObservedAtUtc is { } observed
            ? new VerifiedWindowsGroupMembership(UserId, GroupSids, observed) : null);
    }

    private static WellSiteAutoPilotException ReadFailure(string code) => new(code, FailureKind.Authorization,
        "The execution is not authorized to read this source. Refresh the configuration and request a new Shadow run.");
}
