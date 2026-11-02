using WellSiteAutoPilot.Application.Control;

/// <summary>The pure rules behind target resolution and the catalog's hard limits at dispatch.</summary>
internal static class ControlTargetRuleChecks
{
    public static void Run()
    {
        ExpectId([], [], null, GateCodes.TargetNotBound);
        ExpectId(["A"], [], "A", null);
        ExpectId([], ["A"], "A", null);
        ExpectId(["A"], ["A"], "A", null);
        ExpectId(["A", "A"], ["A"], "A", null);
        ExpectId(["A"], ["B"], null, GateCodes.TargetAmbiguous);
        ExpectId(["A", "B"], [], null, GateCodes.TargetAmbiguous);
        ExpectId([], ["A", "B"], null, GateCodes.TargetAmbiguous);
        ExpectId(["a"], ["A"], null, GateCodes.TargetAmbiguous);
        ExpectId([" ", ""], [""], null, GateCodes.TargetNotBound);

        ConnectionCommandCatalogEntry Entry(bool enabled = true, decimal? min = 0, decimal? max = 12) =>
            new("SET_SPEED", enabled, min, max, null, new Dictionary<string, string>(), 2, 30);

        ExpectCatalog(null, 5, GateCodes.CommandNotInCatalog);
        ExpectCatalog(Entry(enabled: false), 5, GateCodes.CommandNotEnabled);
        ExpectCatalog(Entry(min: null), 5, GateCodes.CatalogOutOfBounds);
        ExpectCatalog(Entry(max: null), 5, GateCodes.CatalogOutOfBounds);
        ExpectCatalog(Entry(), -0.01m, GateCodes.CatalogOutOfBounds);
        ExpectCatalog(Entry(), 12.01m, GateCodes.CatalogOutOfBounds);
        ExpectCatalog(Entry(), 0, null);
        ExpectCatalog(Entry(), 12, null);
        ExpectCatalog(Entry(), 6.5m, null);

        var catalog = new ConnectionCommandCatalog(Guid.NewGuid(), 2, "x", DateTimeOffset.UtcNow, null, [Entry()]);
        Require(ControlTargetRules.Find(catalog, "set_speed")?.Command == "SET_SPEED", "a catalog finds a command ignoring case");
        Require(ControlTargetRules.Find(catalog, "OTHER") is null && ControlTargetRules.Find(null, "SET_SPEED") is null,
            "a missing command or catalog finds nothing");
    }

    private static void ExpectId(string[] imported, string[] configured, string? id, string? code)
    {
        var (actualId, actualCode) = ControlTargetRules.PickExternalId(imported, configured);
        Require(actualId == id && actualCode == code,
            $"[{string.Join(',', imported)}] + [{string.Join(',', configured)}] gave {actualId}/{actualCode}, expected {id}/{code}");
    }

    private static void ExpectCatalog(ConnectionCommandCatalogEntry? entry, decimal value, string? code) =>
        Require(ControlTargetRules.CheckCatalog(entry, value) == code,
            $"catalog check of {value} gave {ControlTargetRules.CheckCatalog(entry, value)}, expected {code}");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Control target rule check failed: " + message);
    }
}
