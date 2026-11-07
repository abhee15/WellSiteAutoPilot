using WellSiteAutoPilot.Api.Contracts.Connections;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Connections.Discovery;
using WellSiteAutoPilot.Application.Connections.Importing;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Server.Connections;

internal static class ConnectionMapping
{
    public static AssetOperationsResponse ToResponse(AssetOperationsSnapshot snapshot) => new(
        snapshot.ConnectionId, snapshot.ConnectionRevision, snapshot.AssetId, snapshot.AdapterKey, snapshot.ConnectionState,
        snapshot.Operations.Select(operation => new AssetOperationEligibilityResponse(operation.Key, operation.ContractVersion,
            WellSiteAutoPilot.Server.Contracts.ContractEnumMapping.ToContract(operation.State), operation.Reason)).ToArray());

    public static AdapterOperationsResponse ToOperations(AdapterDescriptor adapter) => new(
        adapter.Key, adapter.DisplayName, adapter.Description,
        adapter.ReadOperations().Select(operation => new AdapterOperationResponse(
            operation.Key, operation.DisplayName, operation.Description, operation.Capability.ToString(), operation.CapabilityId,
            operation.ContractVersion, "ReadOnly", ToSchema(operation.Request), ToSchema(operation.Response),
            operation.TimestampSemantics, operation.QualitySemantics,
            new(operation.Limits.MaximumQuantities, operation.Limits.MaximumValueBytes, operation.Limits.MaximumReplyBytes,
                operation.Limits.TimeoutSeconds, operation.Limits.MaximumWindowDays, operation.Limits.MaximumSamples, operation.Limits.MaximumSourceBytes),
            operation.Quantity, operation.ResourceKind, operation.Unit, operation.StructuredSchemaId,
            operation.StructuredResponse is null ? null : ToSchema(operation.StructuredResponse),
            operation.Documentation is not { } docs ? null : new(docs.Invocation, docs.ExecutionBoundary,
                docs.RequestExampleJson, docs.ResponseExampleJson, docs.ProviderReference))).ToArray(),
        adapter.ReadCapabilityGroups().Select(group => new AdapterCapabilityGroupResponse(group.Key, group.DisplayName,
            group.Description, group.ContractVersion, group.OperationKeys)).ToArray());

    private static AdapterOperationSchemaResponse ToSchema(AdapterOperationSchema schema) => new(schema.Id,
        schema.Fields.Select(field => new AdapterOperationFieldResponse(field.Name, field.DataType, field.Required, field.Description)).ToArray(), schema.DataType);

    public static AdapterResponse ToResponse(AdapterDescriptor adapter) => new(
        adapter.Key, adapter.DisplayName, adapter.Description, adapter.SettingsVersion,
        adapter.Fields.Select(field => new AdapterSettingFieldResponse(
            field.Key, field.Label, field.HelpText, field.Kind.ToString(), field.Required, field.MaxLength)).ToArray(),
        adapter.Capabilities.Select(capability => capability.Name).ToArray(),
        adapter.CanDiscover,
        (adapter.GroupingFields ?? []).Select(field => new DiscoveryFieldResponse(field.Key, field.Label)).ToArray(),
        (adapter.Commands ?? []).Select(command => new AdapterCommandResponse(
            command.Name, command.DisplayName, command.Description, command.Quantity, command.Unit, command.ValueKind.ToString(),
            command.TargetFields.Select(field => new AdapterSettingFieldResponse(
                field.Key, field.Label, field.HelpText, field.Kind.ToString(), field.Required, field.MaxLength)).ToArray(),
            command.Verification.ToString(), command.ReadbackQuantity, command.DefaultSettleSeconds, command.MaximumSettleSeconds,
            command.DefaultDeadlineSeconds, command.MaximumDeadlineSeconds)).ToArray());

    /// <summary>The summary a person starts from: totals, facets and the first <see cref="ConnectionService.SampleSize"/> objects.</summary>
    public static DiscoveryResponse ToResponse(DiscoverySnapshot snapshot, ImportHierarchyStatus? hierarchy = null) => new(
        snapshot.Result.Complete,
        DiscoveryScopes.Page(snapshot.Result.Items, 0, ConnectionService.SampleSize).Select(ToObject).ToArray(),
        snapshot.Result.Warnings, snapshot.Result.ActingAs, snapshot.Result.Items.Count, snapshot.Id,
        DiscoveryScopes.Summarize(snapshot.Result.Items, snapshot.Fields).Select(facet => new DiscoveryFacetResponse(
            facet.Key, facet.Label, facet.Values.Select(value => new DiscoveryFacetValueResponse(value.Value, value.Count)).ToArray(),
            facet.HasMore, facet.KnownCount)).ToArray(),
        snapshot.CreatedAtUtc + DiscoverySnapshotStore.Lifetime,
        snapshot.Fields.Any(field => field.Key == "assetName")
            ? snapshot.Result.Items.Select(item => item.Metadata.GetValueOrDefault("assetId"))
                .Where(id => !string.IsNullOrWhiteSpace(id) && id != "0").Distinct(StringComparer.Ordinal).Count() : null, hierarchy is null ? null : new ImportHierarchyResponse(hierarchy.Status, hierarchy.Levels));

    public static DiscoverySearchResponse ToResponse(IReadOnlyList<DiscoveredAsset> matches, int offset, int limit) => new(
        matches.Count, DiscoveryScopes.Page(matches, offset, limit).Select(ToObject).ToArray());

    private static DiscoveredObjectResponse ToObject(DiscoveredAsset item) => new(
        item.ExternalId, item.DisplayName, item.ProviderType, item.Metadata);

    public static ImportSelection ToSelection(Guid? snapshotId, string? query, Dictionary<string, List<string>>? facets,
        List<DiscoveryObjectKeyRequest>? objects = null) => new(
        snapshotId,
        new DiscoveryScope(query, facets?.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal),
            objects?.Select(key => key is null ? null! : new DiscoveryObjectKey(key.Kind, key.ExternalId)).ToArray()));

    public static ImportPreviewResponse ToResponse(ImportPreview preview) => new(
        preview.Plan.Fingerprint, preview.Plan.Levels, preview.Plan.Placements.Count, preview.Plan.GroupCount,
        preview.Plan.FallbackObjectCount, preview.Plan.RootObjectCount, preview.Plan.Groups.Select(ToResponse).ToArray(),
        preview.Warnings, preview.Conflicts, preview.TypesToCreate, preview.CanImport, preview.ActingAs, preview.AlreadyImported, preview.AdoptingHierarchy, preview.Hierarchy.Wells.Count, preview.GroupsToAdopt);

    public static ImportResultResponse ToResponse(ImportOutcome outcome) => new(
        outcome.WellsCreated, outcome.GroupsCreated, outcome.GroupsReused, outcome.AlreadyImported, outcome.TypesCreated, outcome.HierarchyAdopted);

    private static ImportGroupResponse ToResponse(PlanGroup group) => new(
        group.Name, group.IsFallback, group.ObjectCount, group.SampleNames, group.Children.Select(ToResponse).ToArray(), group.Key, group.SourceIdentity);

    public static ConnectionResponse ToResponse(ConnectionRecord connection) => new(
        connection.Id, connection.Name, connection.AdapterKey, connection.SettingsVersion, connection.Settings,
        connection.IsEnabled, connection.Revision, connection.CreatedAtUtc, connection.UpdatedAtUtc,
        connection.LastTest is null ? null : new ConnectionTestResponse(connection.LastTest.Outcome,
            connection.LastTest.Message, connection.LastTest.ActingAs, connection.LastTest.TestedAtUtc,
            connection.LastTest.TestedRevision, connection.LastTest.Source),
        connection.LastTest?.TestedRevision == connection.Revision,
        connection.HasImports);
}
