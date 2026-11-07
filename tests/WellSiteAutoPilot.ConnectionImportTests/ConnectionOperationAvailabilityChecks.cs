using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;

internal static class ConnectionOperationAvailabilityChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var connection = new ConnectionRecord(Guid.NewGuid(), "Example", "foresite", 1, new Dictionary<string, string>(), true, 4, now, now,
            new(AdapterTestOutcomes.Healthy, "OK", null, now, 4));
        var adapter = ForeSiteAdapter.Descriptor;
        Check(ConnectionOperationAvailability.Evaluate(connection, adapter).State == "ConnectionReady", "Tested connection should be ready.");
        Check(ConnectionOperationAvailability.Evaluate(connection with { IsEnabled = false }, adapter).OperationKeys.Count == 0, "Disabled connection advertised reads.");
        Check(ConnectionOperationAvailability.Evaluate(connection with { SettingsVersion = 2 }, adapter).State == "Incompatible", "Settings version mismatch ignored.");
        Check(ConnectionOperationAvailability.Evaluate(connection with { Revision = 5 }, adapter).State == "Unknown", "Stale test used as readiness.");
        Check(ConnectionOperationAvailability.Evaluate(connection, null).OperationKeys.Count == 0, "Missing adapter fell back.");
        Check(ConnectionOperationAvailability.Evaluate(connection, adapter with { Key = "cygnet" }).OperationKeys.Count == 0, "Different provider substituted.");
        Check(ConnectionOperationAvailability.Evaluate(connection with { LastTest = connection.LastTest! with { Outcome = AdapterTestOutcomes.Unreachable } }, adapter).OperationKeys.Count == 0,
            "Failed test advertised usable operations.");
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
