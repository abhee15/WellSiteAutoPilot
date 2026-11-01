using WellSiteAutoPilot.Api.Contracts.Control;
using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Server.Connections;

internal static class CommandCatalogMapping
{
    public static ConnectionCommandCatalogResponse ToResponse(ConnectionCommandCatalogView view) => new(
        view.ConnectionId, view.AdapterKey, view.Version, view.ChangedBy, view.ChangedAtUtc, view.Reason,
        view.Entries.Select(ToDto).ToArray(),
        ConnectionMapping.ToResponse(new AdapterDescriptor(
            view.AdapterKey, view.AdapterKey, string.Empty, 0, [], [], null, 0, view.Available)).Commands);

    public static ConnectionCommandCatalogEntryDto ToDto(ConnectionCommandCatalogEntry entry) => new(
        entry.Command, entry.Enabled, entry.Minimum, entry.Maximum, entry.MaximumStep,
        new Dictionary<string, string>(entry.Target, StringComparer.Ordinal), entry.SettleSeconds, entry.DeadlineSeconds);

    public static ConnectionCommandCatalogEntry ToEntry(ConnectionCommandCatalogEntryDto dto) => new(
        dto.Command, dto.Enabled, dto.Minimum, dto.Maximum, dto.MaximumStep,
        dto.Target ?? new Dictionary<string, string>(), dto.SettleSeconds, dto.DeadlineSeconds);
}
