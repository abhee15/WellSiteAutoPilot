using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Control;

/// <summary>A connection's catalog beside what its adapter could offer, so a person can see what is and is not switched on.</summary>
public sealed record ConnectionCommandCatalogView(
    Guid ConnectionId,
    string AdapterKey,
    int Version,
    string? ChangedBy,
    DateTimeOffset? ChangedAtUtc,
    string? Reason,
    IReadOnlyList<ConnectionCommandCatalogEntry> Entries,
    IReadOnlyList<AdapterCommandDefinition> Available);

/// <summary>
/// Reads and replaces a connection's command catalog. A catalog is judged against the adapter's own command definitions
/// before it is saved, so a site cannot enable something the adapter cannot carry out or set limits the rules refuse.
/// </summary>
public sealed class ConnectionCommandCatalogService(
    IConnectionCommandCatalogRepository catalogs,
    IConnectionRepository connections,
    IGatewayAdapters gateway)
{
    private const int MaximumProblemsReported = 5;

    public async Task<ConnectionCommandCatalogView> GetAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var connection = await FindConnectionAsync(connectionId, cancellationToken);
        var adapter = await FindAdapterAsync(connection.AdapterKey, cancellationToken);
        var catalog = await catalogs.FindAsync(connectionId, cancellationToken);
        return ToView(connection, adapter, catalog);
    }

    public async Task<ConnectionCommandCatalogView> ReplaceAsync(
        Guid connectionId, int expectedVersion, IReadOnlyCollection<ConnectionCommandCatalogEntry>? entries, string? reason,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        var connection = await FindConnectionAsync(connectionId, cancellationToken);
        var adapter = await FindAdapterAsync(connection.AdapterKey, cancellationToken)
            ?? throw new WellSiteAutoPilotException(ConnectionFailureCodes.AdapterUnknown, FailureKind.Validation,
                "The Gateway does not know this connection's adapter, so its commands cannot be judged.");

        var problems = ConnectionCommandCatalogRules.Validate(adapter, entries);
        if (problems.Count > 0)
        {
            var shown = string.Join(" ", problems.Take(MaximumProblemsReported));
            var more = problems.Count > MaximumProblemsReported ? $" ({problems.Count - MaximumProblemsReported} more.)" : string.Empty;
            throw new WellSiteAutoPilotException(ConnectionCommandCatalogFailureCodes.Invalid, FailureKind.Validation, shown + more);
        }

        // Stored under the adapter's own spelling, so every later comparison with a module's command is exact.
        var definitions = (adapter.Commands ?? []).ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var canonical = entries!.Select(entry => entry with { Command = definitions[entry.Command].Name }).ToArray();
        var saved = await catalogs.ReplaceAuthorizedAsync(
            connectionId, expectedVersion, canonical, reason, subject, actor, cancellationToken);
        return ToView(connection, adapter, saved);
    }

    private async Task<ConnectionRecord> FindConnectionAsync(Guid connectionId, CancellationToken cancellationToken) =>
        await connections.FindAsync(connectionId, cancellationToken)
        ?? throw new WellSiteAutoPilotException(ConnectionFailureCodes.NotFound, FailureKind.NotFound, "The connection was not found.");

    private async Task<AdapterDescriptor?> FindAdapterAsync(string adapterKey, CancellationToken cancellationToken) =>
        (await gateway.ListAsync(cancellationToken)).FirstOrDefault(item => string.Equals(item.Key, adapterKey, StringComparison.Ordinal));

    private static ConnectionCommandCatalogView ToView(
        ConnectionRecord connection, AdapterDescriptor? adapter, ConnectionCommandCatalog? catalog) =>
        new(connection.Id, connection.AdapterKey, catalog?.Version ?? 0, catalog?.ChangedBy, catalog?.ChangedAtUtc, catalog?.Reason,
            catalog?.Entries ?? [], adapter?.Commands ?? []);
}
