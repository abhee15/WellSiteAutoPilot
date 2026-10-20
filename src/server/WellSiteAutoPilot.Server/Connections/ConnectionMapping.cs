using WellSiteAutoPilot.Api.Contracts.Connections;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Server.Connections;

internal static class ConnectionMapping
{
    public static AdapterResponse ToResponse(AdapterDescriptor adapter) => new(
        adapter.Key, adapter.DisplayName, adapter.Description, adapter.SettingsVersion,
        adapter.Fields.Select(field => new AdapterSettingFieldResponse(
            field.Key, field.Label, field.HelpText, field.Kind.ToString(), field.Required, field.MaxLength)).ToArray(),
        adapter.Capabilities.Select(capability => capability.Name).ToArray(),
        adapter.CanDiscover,
        (adapter.GroupingFields ?? []).Select(field => new DiscoveryFieldResponse(field.Key, field.Label)).ToArray());

    public static DiscoveryResponse ToResponse(DiscoveryResult result) => new(
        result.Complete,
        result.Items.Select(item => new DiscoveredObjectResponse(
            item.ExternalId, item.DisplayName, item.ProviderType, item.Metadata)).ToArray(),
        result.Warnings, result.ActingAs);

    public static ConnectionResponse ToResponse(ConnectionRecord connection) => new(
        connection.Id, connection.Name, connection.AdapterKey, connection.SettingsVersion, connection.Settings,
        connection.IsEnabled, connection.Revision, connection.CreatedAtUtc, connection.UpdatedAtUtc,
        connection.LastTest is null ? null : new ConnectionTestResponse(connection.LastTest.Outcome,
            connection.LastTest.Message, connection.LastTest.ActingAs, connection.LastTest.TestedAtUtc,
            connection.LastTest.TestedRevision),
        connection.LastTest?.TestedRevision == connection.Revision);
}
