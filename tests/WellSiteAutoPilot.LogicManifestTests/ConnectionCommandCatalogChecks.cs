using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Integration.Contracts.Providers;

internal static class ConnectionCommandCatalogChecks
{
    private static readonly AdapterSettingField Setpoint = new("setpoint", "Setpoint name", "Which setpoint.", AdapterSettingKind.Text, true, 16);

    private static AdapterCommandDefinition Definition(string name, AdapterCommandValueKind kind) =>
        new(name, name, name, "Speed", kind == AdapterCommandValueKind.OnOff ? null : "Hz", kind, [Setpoint],
            AdapterCommandVerification.ReadBack, "Speed", 5, 60, 30, 300);

    private static readonly AdapterDescriptor Adapter = new("x", "X", "X", 1, [], [], null, 0,
        [Definition("SET_SPEED", AdapterCommandValueKind.Number), Definition("SET_COUNT", AdapterCommandValueKind.WholeNumber),
         Definition("PUMP", AdapterCommandValueKind.OnOff)]);

    private static ConnectionCommandCatalogEntry Entry(string command, bool enabled = true, decimal? min = 30, decimal? max = 60,
        decimal? step = 5, string? setpoint = "SP1", int settle = 5, int deadline = 30) =>
        new(command, enabled, min, max, step,
            setpoint is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["setpoint"] = setpoint },
            settle, deadline);

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static bool Refused(params ConnectionCommandCatalogEntry[] entries) =>
        ConnectionCommandCatalogRules.Validate(Adapter, entries).Count > 0;

    public static void Run()
    {
        Expect(ConnectionCommandCatalogRules.Validate(Adapter, []).Count == 0, "An empty catalog must be valid.");
        Expect(ConnectionCommandCatalogRules.Validate(Adapter, [Entry("SET_SPEED")]).Count == 0, "A complete entry must be valid.");
        Expect(ConnectionCommandCatalogRules.Validate(Adapter, null).Count > 0, "A missing catalog must be refused.");

        Expect(Refused(Entry("UNKNOWN")), "A command the adapter does not define must be refused.");
        Expect(Refused(Entry("UNKNOWN", enabled: false)), "An undefined command must be refused even when disabled.");
        Expect(Refused(Entry("SET_SPEED"), Entry("set_speed")), "A command listed twice (any case) must be refused.");
        Expect(!Refused(Entry("set_speed")), "Command names match the adapter's regardless of case.");

        Expect(!Refused(Entry("SET_SPEED", enabled: false, min: null, max: null, setpoint: null)),
            "A disabled entry needs no limits and no target.");
        Expect(Refused(Entry("SET_SPEED", min: null)), "An enabled entry without a minimum must be refused.");
        Expect(Refused(Entry("SET_SPEED", max: null)), "An enabled entry without a maximum must be refused.");
        Expect(Refused(Entry("SET_SPEED", min: 61, max: 60)), "A minimum above the maximum must be refused.");
        Expect(Refused(Entry("SET_SPEED", step: 0)) && Refused(Entry("SET_SPEED", step: -1)), "A step of zero or less must be refused.");
        Expect(!Refused(Entry("SET_SPEED", step: null)), "A step limit is optional.");

        Expect(Refused(Entry("SET_COUNT", min: 1.5m, max: 10)) && Refused(Entry("SET_COUNT", min: 1, max: 10, step: 0.5m)),
            "A whole-number command must have whole limits and step.");
        Expect(!Refused(Entry("SET_COUNT", min: 1, max: 10, step: 2)), "A whole-number command accepts whole limits.");
        Expect(!Refused(Entry("PUMP", min: 0, max: 1, step: null)), "An on/off command is 0 to 1.");
        Expect(Refused(Entry("PUMP", min: 0, max: 2, step: null)) && Refused(Entry("PUMP", min: 0, max: 1, step: 1)),
            "An on/off command must be 0 to 1 with no step.");

        Expect(Refused(Entry("SET_SPEED", setpoint: null)), "A required target field must be present when enabled.");
        Expect(Refused(Entry("SET_SPEED", setpoint: "  ")), "A blank required target field must be refused.");
        Expect(Refused(Entry("SET_SPEED", setpoint: new string('a', 17))), "A target field over its length must be refused.");
        Expect(Refused(Entry("SET_SPEED", setpoint: "SP\n1")), "A target field with control characters must be refused.");
        Expect(ConnectionCommandCatalogRules.Validate(Adapter, [new ConnectionCommandCatalogEntry("SET_SPEED", true, 30, 60, 5,
            new Dictionary<string, string> { ["setpoint"] = "SP1", ["other"] = "x" }, 5, 30)]).Count > 0,
            "A target field the adapter does not define must be refused.");

        Expect(Refused(Entry("SET_SPEED", settle: -1)) && Refused(Entry("SET_SPEED", settle: 61)), "Settle outside the adapter's bound must be refused.");
        Expect(Refused(Entry("SET_SPEED", deadline: 0)) && Refused(Entry("SET_SPEED", deadline: 301)), "A deadline outside the adapter's bound must be refused.");
        Expect(Refused(Entry("SET_SPEED", settle: 30, deadline: 30)), "A deadline not after the settle must be refused.");

        var entry = Entry("SET_SPEED");
        Expect(ConnectionCommandCatalogRules.CheckPolicyLimit("SET_SPEED", 30, 60, entry).Count == 0, "A policy on the catalog edge must be allowed.");
        Expect(ConnectionCommandCatalogRules.CheckPolicyLimit("SET_SPEED", 40, 50, entry).Count == 0, "A policy inside the catalog must be allowed.");
        Expect(ConnectionCommandCatalogRules.CheckPolicyLimit("SET_SPEED", 29, 50, entry).Count > 0, "A policy below the catalog must be refused.");
        Expect(ConnectionCommandCatalogRules.CheckPolicyLimit("SET_SPEED", 40, 61, entry).Count > 0, "A policy above the catalog must be refused.");
        Expect(ConnectionCommandCatalogRules.CheckPolicyLimit("SET_SPEED", 50, 40, entry).Count > 0, "An inverted policy must be refused.");
        Expect(ConnectionCommandCatalogRules.CheckPolicyLimit("SET_SPEED", null, 50, entry).Count > 0 &&
               ConnectionCommandCatalogRules.CheckPolicyLimit("SET_SPEED", 40, null, entry).Count > 0, "An open policy bound must be refused.");
        Expect(ConnectionCommandCatalogRules.CheckPolicyLimit("SET_SPEED", 40, 50, null).Count > 0, "No catalog entry permits nothing.");
        Expect(ConnectionCommandCatalogRules.CheckPolicyLimit("SET_SPEED", 40, 50, Entry("SET_SPEED", enabled: false)).Count > 0,
            "A disabled entry permits nothing.");

        Expect(ConnectionCommandCatalogRules.StepAllowed(entry, 40, 45) && ConnectionCommandCatalogRules.StepAllowed(entry, 45, 40),
            "A move equal to the step limit must be allowed in both directions.");
        Expect(!ConnectionCommandCatalogRules.StepAllowed(entry, 40, 45.1m) && !ConnectionCommandCatalogRules.StepAllowed(entry, 45.1m, 40),
            "A move over the step limit must be refused in both directions.");
        Expect(ConnectionCommandCatalogRules.StepAllowed(Entry("SET_SPEED", step: null), 0, 1000), "No step limit allows any move.");
    }
}
