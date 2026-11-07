using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Connections;

/// <summary>Pure connection readiness, never execution authorization or asset/point availability.</summary>
public sealed record ConnectionOperationAvailability(string State, string Reason, IReadOnlyList<string> OperationKeys)
{
    public static ConnectionOperationAvailability Evaluate(ConnectionRecord connection, AdapterDescriptor? adapter)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (adapter is null || adapter.Key != connection.AdapterKey)
            return new("Unsupported", "The saved adapter is not installed. No other provider will be selected.", []);
        if (adapter.SettingsVersion != connection.SettingsVersion)
            return new("Incompatible", "The saved settings need review for this adapter version.", []);
        if (!connection.IsEnabled)
            return new("Disabled", "The connection is disabled.", []);
        var keys = adapter.ReadOperations().Select(operation => operation.Key).ToArray();
        if (keys.Length == 0)
            return new("Unsupported", "This adapter declares no implemented read operations.", []);
        if (connection.LastTest is not { } test || test.TestedRevision != connection.Revision)
            return new("Unknown", "Test the current connection settings to check provider access. Asset and point availability require a separate authorized read.", keys);
        if (test.Outcome != AdapterTestOutcomes.Healthy)
            return new("Unavailable", "The latest connection test did not succeed.", []);
        return new("ConnectionReady", "The latest test succeeded for this revision. Individual asset permissions, point mappings and provider compatibility still apply at read time.", keys);
    }
}
