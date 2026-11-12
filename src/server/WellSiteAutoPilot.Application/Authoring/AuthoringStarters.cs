namespace WellSiteAutoPilot.Application.Authoring;

public sealed record Starter(string Id, string Title, string Summary, ModuleDraft Draft);

/// <summary>Worked examples that teach the model: each one is a complete, valid draft shared by the UI and the tests.</summary>
public static class AuthoringStarters
{
    private static InputSource Surv(string field, string? basis = "Current") => new("surveillance:" + field, basis);

    public static readonly IReadOnlyList<Starter> All =
    [
        new("location", "Well location",
            "The simplest module: read two values and publish them. Shows what a provider can answer for an asset.",
            new ModuleDraft("Well location", "Publishes surface latitude and longitude.",
                [new("Latitude", [new("well:SurfaceLatitude")]), new("Longitude", [new("well:SurfaceLongitude")])], [], [],
                [new("Latitude_out", "Latitude", Unit: "deg"), new("Longitude_out", "Longitude", Unit: "deg")])),

        new("snapshot", "Surveillance snapshot",
            "Several live readings side by side. Pump fillage is optional, so wells without it still run.",
            new ModuleDraft("Surveillance snapshot", "Casing, tubing and pump fillage from live surveillance.",
                [new("Casing", [Surv("CasingPressure")]), new("Tubing", [Surv("TubingPressure")]),
                 new("Fillage", [Surv("PumpFillage")], Required: false)], [], [],
                [new("Casing_out", "Casing", Unit: "psi"), new("Tubing_out", "Tubing", Unit: "psi"),
                 new("Fillage_out", "coalesce(Fillage, -1)", Unit: "%")])),

        new("fallback", "Casing pressure with fallback",
            "One input with two ordered sources: live surveillance first, then the latest well test when the controller reports none.",
            new ModuleDraft("Casing pressure", "Prefers live data, falls back to the well test.",
                [new("Casing", [Surv("CasingPressure"), new("welltest:AverageCasingPressure", "TestSample")])], [], [],
                [new("Casing_out", "Casing", Unit: "psi")])),

        new("differential", "Casing minus tubing",
            "A named intermediate result. Invalid data stays unavailable instead of turning into a zero.",
            new ModuleDraft("Pressure differential", "Casing minus tubing, with its absolute value.",
                [new("Casing", [Surv("CasingPressure"), new("welltest:AverageCasingPressure", "TestSample")]),
                 new("Tubing", [Surv("TubingPressure"), new("welltest:AverageTubingPressure", "TestSample")])], [],
                [new("Differential", "Casing - Tubing")],
                [new("Differential_out", "Differential", Unit: "psi"), new("Magnitude_out", "abs(Differential)", Unit: "psi")])),

        new("threshold", "Pressure status",
            "A parameter and a decision. Change the limit and re-test to see the result and the trace update.",
            new ModuleDraft("Pressure status", "Flags high casing pressure against an adjustable limit.",
                [new("Casing", [Surv("CasingPressure"), new("welltest:AverageCasingPressure", "TestSample")])],
                [new("HighLimit", "250", "High casing limit (psi)")],
                [new("IsHigh", "Casing > HighLimit"), new("Margin", "HighLimit - Casing")],
                [new("High_out", "IsHigh"), new("Margin_out", "Margin", Unit: "psi"),
                 new("Status_out", "if(IsHigh, \"High\", \"Normal\")")])),

        new("setpoint", "Set point change (dry run)",
            "Calculates a change to a controller set point. It shows the before and after values and the allowed range. Nothing is sent.",
            new ModuleDraft("Ideal PF bump", "Raises IdealPF by a step when the well is running.",
                [new("Spm", [Surv("CurrentSPM")])], [new("Step", "1", "Increase by")],
                [new("Running", "Spm > 0")],
                [new("IdealPF_change", "if(Running, Step, 0)", "setpoint", Target: "IdealPF")]))
    ];
}
