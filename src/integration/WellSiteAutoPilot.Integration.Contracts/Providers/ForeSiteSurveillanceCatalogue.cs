namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>
/// One numeric field of the ForeSite surveillance status document, in ForeSite's default (imperial)
/// unit system. <see cref="Quantity"/> is the module-facing name, <see cref="Field"/> the document field.
/// </summary>
public sealed record ForeSiteSurveillanceQuantity(string Quantity, string Field, string Unit, IReadOnlyList<string> WellTypes)
{
    public const string CountUnit = "count";

    /// <summary>A surveillance value is stamped with the well's last good scan, never with the retrieval time.</summary>
    public const string TimeSource = "LastGoodScanTime";

    /// <summary>The percentage fields are bounded; other fields only have to be finite numbers.</summary>
    public bool IsPercent => Unit == "%";
}

/// <summary>
/// The provider-owned list of surveillance quantities an author may choose, independent of any asset.
/// Derived from ForeSite's WellQuantity enum and the per-well-type status DTOs. Whether a given well
/// reports a value is decided when the module runs, never by this list.
/// </summary>
public static class ForeSiteSurveillanceCatalogue
{
    private static readonly string[] Rrl = ["RRL"];

    public static IReadOnlyList<ForeSiteSurveillanceQuantity> All { get; } =
    [
        new("CurrentSPM", "CurrentSPM", "SPM", Rrl),
        new("AverageSPM", "AverageSPM", "SPM", Rrl),
        new("CyclesToday", "CyclesToday", ForeSiteSurveillanceQuantity.CountUnit, Rrl),
        new("CyclesYesterday", "CyclesYesterday", ForeSiteSurveillanceQuantity.CountUnit, Rrl),
        new("IdleTimeToday", "IdleTimeToday", "s", Rrl),
        new("IdleTimeYesterday", "IdleTimeYesterday", "s", Rrl),
        new("EffectiveRunTimePercentage", "EffectiveRunTimePercentage", "%", Rrl),
        new("StrokesToday", "StrokesToday", ForeSiteSurveillanceQuantity.CountUnit, Rrl),
        new("StrokesYesterday", "StrokesYesterday", ForeSiteSurveillanceQuantity.CountUnit, Rrl),
        new("RunTimeToday", "RunTimeToday", "s", Rrl),
        new("RunTimeYesterday", "RunTimeYesterday", "s", Rrl),
        new("AveragePumpFillage", "AveragePumpFillage", "%", Rrl),
        // The module-facing name predates the catalogue; the document field is CurrentPumpFillage.
        new("PumpFillage", "CurrentPumpFillage", "%", Rrl),
        new("InferredProductionToday", "InferredProductionToday", "bbl/d", Rrl),
        new("InferredProductionYesterday", "InferredProductionYesterday", "bbl/d", Rrl),
        new("CardArea", "CardArea", "hp", Rrl),
        new("DailyMinimumLoad", "DailyMinimumLoad", "lb", Rrl),
        new("DailyMaximumLoad", "DailyMaximumLoad", "lb", Rrl),
        new("CasingPressure", "CasingPressure", "psia", ["RRL", "ESP", "GLift", "PLift", "PCP"]),
        new("TubingPressure", "TubingPressure", "psia", ["RRL", "ESP", "GLift", "PLift", "NF", "PCP", "GInj", "WInj", "SInj", "CInj"]),
        new("TubingHeadTemperature", "TubingHeadTemperature", "F", ["RRL", "ESP", "GLift", "NF", "PCP", "GInj", "WInj", "SInj", "CInj"]),
        new("PumpIntakePressure", "PumpIntakePressure", "psia", ["ESP", "PCP"]),
        new("PumpDischargePressure", "PumpDischargePressure", "psia", ["ESP", "PCP"]),
        new("GasInjectionRate", "GasInjectionRate", "Mscf/d", ["GLift", "PLift", "GInj", "CInj"]),
        new("DHGaugePressure", "DHGaugePressure", "psia", ["RRL", "GLift", "NF", "GInj", "WInj", "SInj", "CInj"]),
        new("FlowLinePressure", "FlowLinePressure", "psia", ["ESP", "GLift", "PLift", "NF", "PCP", "WInj", "CInj"]),
        new("CasingGasRate", "CasingGasRate", "Mscf/d", ["ESP", "PCP"]),
        new("ChokeDiameter", "ChokeDiameter", "1/64in", ["ESP", "GLift", "NF"]),
        new("MotorFrequency", "MotorFrequency", "Hz", ["ESP"]),
        new("Vibration", "Vibration", "g", ["ESP"]),
        new("MotorTemperature", "MotorTemperature", "F", ["ESP", "PCP"]),
        new("MotorAmps", "MotorAmps", "A", ["ESP", "PCP"]),
        new("MotorVolts", "MotorVolts", "V", ["ESP", "PCP"]),
        new("GasInjectionVolumeToday", "GasInjectionVolumeToday", "Mscf", ["CInj"]),
        new("InjectionTime", "InjectionTime", "s", ["CInj"]),
        new("DifferentialPressure", "DifferentialPressure", "psi", ["PLift", "PCP"]),
        new("LoadFactor", "LoadFactor", ForeSiteSurveillanceQuantity.CountUnit, ["PLift"]),
        new("PumpSpeed", "PumpSpeed", "rpm", ["PCP"]),
        new("PumpTorque", "PumpTorque", "ft-lbs", ["PCP"]),
        new("SteamInjectionVolumeToday", "SteamInjectionVolumeToday", "bbl", ["RRL", "SInj"]),
        new("SteamInjectionRate", "SteamInjectionRate", "bbl/d", ["RRL", "SInj"]),
        new("FlowLineTemperature", "FlowLineTemperature", "F", ["CInj"]),
        new("DHGaugeTemperature", "DHGaugeTemperature", "F", ["CInj"])
    ];

    private static readonly Dictionary<string, ForeSiteSurveillanceQuantity> ByQuantity =
        All.ToDictionary(item => item.Quantity, StringComparer.Ordinal);

    public static IReadOnlySet<string> Quantities { get; } = new HashSet<string>(ByQuantity.Keys, StringComparer.Ordinal);

    public static ForeSiteSurveillanceQuantity? Find(string? quantity) =>
        quantity is not null && ByQuantity.TryGetValue(quantity, out var item) ? item : null;
}
