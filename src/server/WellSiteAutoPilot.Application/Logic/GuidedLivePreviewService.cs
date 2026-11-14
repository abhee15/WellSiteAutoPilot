using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>Runs a saved draft against one authorized imported asset using current, read-only measurements.</summary>
public sealed class GuidedLivePreviewService(GuidedModuleDraftService drafts, IAssetRepository assets,
    IAssetConnectionSourceReader sources, IConnectionRepository connections, IRequestAccessRepository access,
    IExecutionMeasurementGateway gateway, TimeProvider clock)
{
    public async Task<GuidedLivePreview> PreviewAsync(Guid draftId, int expectedRevision, Guid assetId,
        string? correlationId, AccessSubject subject, CancellationToken cancellationToken = default)
    {
        var draft = await drafts.GetAsync(draftId, subject, cancellationToken);
        draft.RequireRevision(expectedRevision);
        var artifact = await DeclarativeThresholdPackageBuilder.BuildAsync(draft.Content.Manifest, draft.Content.Definition, cancellationToken);
        var module = artifact.Package.DeclarativeModule!;
        var initial = await ReadContextAsync();
        var rule = AdapterReadRules.Find(initial.Connection.AdapterKey);
        var manifest = artifact.Package.Manifest;
        if (rule is null || initial.Binding.ResourceKind != rule.ResourceKind ||
            (manifest.RequiredAdapterKey is { } requiredAdapter && !string.Equals(requiredAdapter, initial.Connection.AdapterKey, StringComparison.OrdinalIgnoreCase)) ||
            manifest.DataRequirements.Any(input => !rule.AllowsCurrentRead(input.Quantity, input.RequiredCapability, input.ValueSchema?.SchemaId)))
            throw new WellSiteAutoPilotException("GUIDED_LIVE_INPUT_UNSUPPORTED", FailureKind.Validation,
                "This connection does not support the draft inputs.");

        var role = manifest.AssetRequirements.Single();
        var assetType = await assets.GetAssetTypeAuthorizedAsync(subject, initial.AssetTypeId, cancellationToken);
        if (assetType is not { IsActive: true } || (role.RequiredAssetTypeKey is { } requiredType &&
            !string.Equals(requiredType, assetType.Key, StringComparison.OrdinalIgnoreCase)) ||
            role.RequiredTraits.Count != 0)
            throw new WellSiteAutoPilotException("GUIDED_LIVE_ASSET_UNSUPPORTED", FailureKind.Validation,
                "The sample asset does not meet the draft asset requirements.");

        var readings = new List<GuidedLiveReading>(manifest.DataRequirements.Count);
        var first = await ObserveAsync(module.Input.Id, module.Input.Quantity);
        var second = module.Subtrahend is { } input ? await ObserveAsync(input.Id, input.Quantity) : null;
        await RequireCurrentContextAsync();
        var currentDraft = await drafts.GetAsync(draftId, subject, cancellationToken);
        currentDraft.RequireRevision(expectedRevision);
        var failure = readings.Any(item => item.TimestampKind == "UnverifiedScan") ? "EXECUTION_INPUT_TIMESTAMP_UNVERIFIED" : null;
        var runId = Guid.NewGuid();
        var sample = new DeclarativeThresholdSample(runId, correlationId ?? runId.ToString("D"), assetId, clock.GetUtcNow(), "{}", "{}", first, second);
        var evidence = failure is null
            ? await DeclarativeThresholdPreview.EvaluateAsync(artifact, sample, cancellationToken)
            : new DeclarativeThresholdEvidence(artifact.Package.Sha256, sample, null, failure);
        return new(evidence, readings);

        async Task<LogicInputValue?> ObserveAsync(string id, string quantity)
        {
            await RequireCurrentContextAsync();
            var value = await gateway.GetCurrentAsync(new(initial.Connection.AdapterKey, initial.Connection.Id,
                initial.Connection.Revision, initial.Binding.ExternalId, quantity,
                initial.Connection.Settings.ToDictionary(entry => entry.Key, entry => (string?)entry.Value, StringComparer.Ordinal)),
                cancellationToken);
            readings.Add(new(id, value.Quantity, value.Value, value.Unit, value.TimestampUtc, value.Quality, value.TimestampKind));
            return value.Value is { } number && value.StructuredValue is null
                ? new(id, assetId, value.Quantity, number, value.Unit, value.TimestampUtc, value.Quality, value.Source)
                : null;
        }

        async Task RequireCurrentContextAsync()
        {
            var current = await ReadContextAsync();
            if (current.Connection.Id != initial.Connection.Id || current.Connection.Revision != initial.Connection.Revision ||
                current.Connection.AdapterKey != initial.Connection.AdapterKey || current.Binding != initial.Binding ||
                current.AssetTypeId != initial.AssetTypeId)
                throw new WellSiteAutoPilotException("GUIDED_LIVE_SOURCE_CHANGED", FailureKind.Conflict,
                    "The sample asset or connection changed. Select it again.");
        }

        async Task<(ConnectionRecord Connection, AssetConnectionBinding Binding, Guid AssetTypeId)> ReadContextAsync()
        {
            if (!await access.HasDeploymentPermissionAsync(subject, WellSitePermissions.GuidedDraftManage, cancellationToken) ||
                !await access.HasDeploymentPermissionAsync(subject, WellSitePermissions.ConnectionsRead, cancellationToken))
                throw new WellSiteAutoPilotException("GUIDED_LIVE_ACCESS_REQUIRED", FailureKind.Authorization,
                    "Authoring and connection access are required for live preview.");
            var asset = await assets.GetAuthorizedAsync(subject, assetId, cancellationToken);
            if (asset is not { IsActive: true, OwnerConnectionId: { } connectionId }) throw SourceUnavailable();
            var source = await sources.GetConnectionSourceAuthorizedAsync(subject, assetId, connectionId, cancellationToken);
            if (source is not { IsActive: true } || source.Bindings.Count != 1) throw SourceUnavailable();
            var connection = await connections.FindAsync(connectionId, cancellationToken);
            if (connection is not { IsEnabled: true }) throw SourceUnavailable();
            return (connection, source.Bindings[0], asset.AssetTypeId);
        }
    }

    private static WellSiteAutoPilotException SourceUnavailable() => new("GUIDED_LIVE_SOURCE_UNAVAILABLE",
        FailureKind.NotFound, "An active, authorized imported asset with one source is required.");
}

public sealed record GuidedLivePreview(DeclarativeThresholdEvidence Evidence, IReadOnlyList<GuidedLiveReading> Readings);
public sealed record GuidedLiveReading(string InputId, string Quantity, decimal? Value, string Unit,
    DateTimeOffset TimestampUtc, string Quality, string? TimestampKind);
