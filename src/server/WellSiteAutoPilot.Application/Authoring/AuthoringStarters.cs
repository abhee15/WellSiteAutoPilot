namespace WellSiteAutoPilot.Application.Authoring;

/// <summary>A starter is a portable definition, the default bindings for this system, and what a user needs to know to use it.</summary>
public sealed record Starter(string Id, string Title, string Summary, ModuleDraft Draft, ModuleBindings? Bindings = null, string[]? Teaches = null);

/// <summary>Worked examples that teach the model: each is a complete, valid draft with stored examples, shared by the UI and the tests.</summary>
public static class AuthoringStarters
{
    private static Dictionary<string, string> D(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static List<BindingSource> LiveThenTest(string quantity) => [new(quantity, "Current"), new(quantity, "TestSample")];

    public static readonly IReadOnlyList<Starter> All =
    [
        new("location", "Well location",
            "The simplest module: read two values and publish them. Shows what a provider can answer for an asset.",
            new ModuleDraft("Well location", "Publishes surface latitude and longitude.",
                [new("Latitude", "Latitude", "deg", Role: "well"), new("Longitude", "Longitude", "deg", Role: "well")], [], [],
                [new("Latitude_out", "Latitude", Unit: "deg"), new("Longitude_out", "Longitude", Unit: "deg")],
                [new("Houston", D(("Latitude", "29.76"), ("Longitude", "-95.37")), D(("Latitude_out", "29.76"), ("Longitude_out", "-95.37")))]),
            Teaches: ["inputs", "outputs"]),

        new("snapshot", "Surveillance snapshot",
            "Several live readings side by side. Pump fillage is optional, so wells without it still run. Readings older than 15 minutes are refused.",
            new ModuleDraft("Surveillance snapshot", "Casing, tubing and pump fillage from live surveillance.",
                [new("Casing", "CasingPressure", "psia", "Current", 900), new("Tubing", "TubingPressure", "psia", "Current", 900),
                 new("Fillage", "PumpFillage", "%", "Current", 900, Required: false)], [], [],
                [new("Casing_out", "Casing", Unit: "psia"), new("Tubing_out", "Tubing", Unit: "psia"),
                 new("Fillage_out", "coalesce(Fillage, -1)", Unit: "%")],
                [new("Typical", D(("Casing", "120"), ("Tubing", "80"), ("Fillage", "65")),
                    D(("Casing_out", "120"), ("Tubing_out", "80"), ("Fillage_out", "65"))),
                 new("No fillage", D(("Casing", "120"), ("Tubing", "80")),
                    D(("Casing_out", "120"), ("Tubing_out", "80"), ("Fillage_out", "-1")))]),
            Teaches: ["optional inputs", "freshness"]),

        new("fallback", "Casing pressure with fallback",
            "One input with two ordered sources: live surveillance first, then the latest well test when the controller reports none.",
            new ModuleDraft("Casing pressure", "Prefers live data, falls back to the well test.",
                [new("Casing", "CasingPressure", "psi")], [], [],
                [new("Casing_out", "Casing", Unit: "psi")],
                [new("Value passes through", D(("Casing", "150")), D(("Casing_out", "150")))]),
            new ModuleBindings(new() { ["Casing"] = LiveThenTest("CasingPressure") }),
            ["bindings", "fallback order"]),

        new("differential", "Casing minus tubing",
            "A named intermediate result. Pressures with different bases cannot be mixed by accident, and missing data stays unavailable instead of turning into a zero.",
            new ModuleDraft("Pressure differential", "Casing minus tubing, with its absolute value.",
                [new("Casing", "CasingPressure", "psi"), new("Tubing", "TubingPressure", "psi")], [],
                [new("Differential", "Casing - Tubing")],
                [new("Differential_out", "Differential", Unit: "psi"), new("Magnitude_out", "abs(Differential)", Unit: "psi")],
                [new("Casing above tubing", D(("Casing", "150"), ("Tubing", "90")), D(("Differential_out", "60"), ("Magnitude_out", "60"))),
                 new("Tubing above casing", D(("Casing", "50"), ("Tubing", "90")), D(("Differential_out", "-40"), ("Magnitude_out", "40"))),
                 new("Missing tubing", D(("Casing", "50")), D(("Differential_out", "unavailable")))]),
            new ModuleBindings(new() { ["Casing"] = LiveThenTest("CasingPressure"), ["Tubing"] = LiveThenTest("TubingPressure") }),
            ["intermediate steps", "units", "missing data"]),

        new("threshold", "Pressure status",
            "A parameter and a decision. Change the limit and re-test to see the result and the trace update.",
            new ModuleDraft("Pressure status", "Flags high casing pressure against an adjustable limit.",
                [new("Casing", "CasingPressure", "psia")],
                [new("HighLimit", "250", "psia", "High casing limit")],
                [new("IsHigh", "Casing > HighLimit"), new("Margin", "HighLimit - Casing")],
                [new("High_out", "IsHigh"), new("Margin_out", "Margin", Unit: "psia"),
                 new("Status_out", "if(IsHigh, \"High\", \"Normal\")")],
                [new("Below the limit", D(("Casing", "200")), D(("High_out", "false"), ("Margin_out", "50"), ("Status_out", "Normal"))),
                 new("Above the limit", D(("Casing", "300")), D(("High_out", "true"), ("Margin_out", "-50"), ("Status_out", "High"))),
                 new("Lower limit", D(("Casing", "200")), D(("High_out", "true")), D(("HighLimit", "150")))]),
            new ModuleBindings(new() { ["Casing"] = LiveThenTest("CasingPressure") }),
            ["parameters", "decisions"]),

        new("setpoint", "Set point proposal (dry run)",
            "Proposes an absolute value for a controller set point, only while the well is running and only inside the limits. Nothing is sent.",
            new ModuleDraft("Ideal PF target", "Proposes setting IdealPF to a target value while the well is running.",
                [new("Spm", "PumpSpm", Role: "well", MaxAgeSeconds: 900)],
                [new("Target", "40", "%", "Target Ideal PF")],
                [],
                [new("IdealPF_proposed", "Target", "setpoint", "%", "IdealPF", new(0, 100), "Spm > 0")],
                [new("Running", D(("Spm", "6")), D(("IdealPF_proposed", "40"))),
                 new("Stopped", D(("Spm", "0")), D(("IdealPF_proposed", "unavailable"))),
                 new("Target above the limit", D(("Spm", "6")), D(("IdealPF_proposed", "unavailable")), D(("Target", "120")))]),
            new ModuleBindings(Targets: new() { ["IdealPF_proposed"] = "IdealPF" }),
            ["set points", "limits", "conditions"])    ];
}
