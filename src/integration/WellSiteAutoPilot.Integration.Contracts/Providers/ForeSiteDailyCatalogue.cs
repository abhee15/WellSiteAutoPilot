namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>
/// One numeric field of a ForeSite daily average record, in ForeSite's default (imperial) unit system.
/// <paramref name="ProviderFixedUnit"/> marks a field whose record never states a unit (ForeSite's daily
/// record has no unit for RunTime, which it uses as hours of a 24 hour day), so the catalogue supplies it.
/// </summary>
public sealed record ForeSiteDailyQuantity(string Quantity, string Unit, bool ProviderFixedUnit = false)
{
    public const string CountUnit = "count";

    /// <summary>
    /// A daily value belongs to its production day (StartDateTime to EndDateTime), never to a scan time.
    /// </summary>
    public const string TimeSource = "ProductionDay";
}

/// <summary>
/// The provider-owned list of daily quantities an author may choose, independent of any asset. Names are the
/// provider's own field names and units are the ones its daily response states in the default unit system.
/// Whether a given well reports a value is decided when the module runs, never by this list.
/// </summary>
public static class ForeSiteDailyCatalogue
{
    public static IReadOnlyList<ForeSiteDailyQuantity> All { get; } =
    [
        new("RunTime", "h", ProviderFixedUnit: true),
        new("AllocatedGasVolume", "Mscf"),
        new("AllocatedOilVolume", "STB"),
        new("AllocatedWaterVolume", "STB"),
        new("GasInjectionRate", "Mscf/d"),
        new("GasInjectionVolumeToday", "Mscf"),
        new("GasInjectionVolumeYesterday", "Mscf"),
        new("GasMeasuredRate", "Mscf/d"),
        new("GasRateAllocated", "Mscf/d"),
        new("GasRateInferred", "Mscf/d"),
        new("InferredGasVolume", "Mscf"),
        new("InferredOilVolume", "STB"),
        new("InferredWaterVolume", "STB"),
        new("MeasuredGasVolume", "Mscf"),
        new("MeasuredOilVolume", "STB"),
        new("MeasuredWaterVolume", "STB"),
        new("OilMeasuredRate", "STB/d"),
        new("OilRateAllocated", "STB/d"),
        new("OilRateInferred", "STB/d"),
        new("SteamInjectionRate", "bbl/d"),
        new("SteamInjectionVolumeToday", "bbl"),
        new("SteamInjectionVolumeYesterday", "bbl"),
        new("TotalFluidRateAllocated", "STB/d"),
        new("TotalFluidRateInferred", "STB/d"),
        new("TotalFluidRateMeasured", "STB/d"),
        new("WaterInjectionRate", "STB/d"),
        new("WaterInjectionVolumeYesterday", "STB"),
        new("WaterMeasuredRate", "STB/d"),
        new("WaterRateAllocated", "STB/d"),
        new("WaterRateInferred", "STB/d"),
        new("AverageCycleAfterFlowTime", "min"),
        new("AverageCycleBuildTime", "min"),
        new("AverageCycleFallTime", "min"),
        new("AverageCycleGasVolume", "Mscf"),
        new("AverageCycleOilVolume", "STB"),
        new("AverageCycleRiseTime", "min"),
        new("AverageCycleTime", "min"),
        new("AverageCycleWaterVolume", "STB"),
        new("AverageMaximumCasingPressure", "psia"),
        new("AverageMaximumTubingPressure", "psia"),
        new("AverageMinimumCasingPressure", "psia"),
        new("AverageMinimumLoadFactor", "fraction"),
        new("AverageMinimumTubingPressure", "psia"),
        new("AveragePlungerRiseSpeed", "ft/min"),
        new("CHP", "psia"),
        new("CalculatedPDP", "psia"),
        new("CalculatedPIP", "psia"),
        new("CasingGasRate", "Mscf/d"),
        new("ChokeDiameter", "1/64in"),
        new("DHPG", "psia"),
        new("DHTG", "F"),
        new("DifferentialPressure", "psi"),
        new("DriveHeadShaftTorqueLoading", "%"),
        new("DriveHeadSpeedLoading", "%"),
        new("EffectiveRodStress", "psia"),
        new("EffectiveRodStressLoading", "%"),
        new("FBHP", "psia"),
        new("FLP", "psia"),
        new("FLT", "F"),
        new("FreeGasAtPumpIntake", "%"),
        new("GasInjectionRateOptimized", "Mscf/d"),
        new("GasJectionDepth", "ft"),
        new("GasJectionDepthActual", "ft"),
        new("InjectionPressure", "psia"),
        new("InjectionTime", "s"),
        new("MotorAmps", "A"),
        new("MotorFrequency", "Hz"),
        new("MotorTemperature", "F"),
        new("MotorVolts", "V"),
        new("NumberOfCycles", ForeSiteDailyQuantity.CountUnit),
        new("PDP", "psia"),
        new("PIP", "psia"),
        new("PlungerArrivalVelocity", "ft/min"),
        new("PolishedRodAxialLoad", "lb"),
        new("PolishedRodTorque", "ft-lbs"),
        new("PolishedRodTorqueLoading", "%"),
        new("PumpAxialLoad", "lb"),
        new("PumpPressureLoading", "%"),
        new("PumpSpeed", "rpm"),
        new("PumpTorque", "ft-lbs"),
        new("THP", "psia"),
        new("THT", "F"),
        new("Vibration", "g"),
    ];

    private static readonly Dictionary<string, ForeSiteDailyQuantity> ByQuantity =
        All.ToDictionary(item => item.Quantity, StringComparer.Ordinal);

    public static ForeSiteDailyQuantity? Find(string? quantity) =>
        quantity is not null && ByQuantity.TryGetValue(quantity, out var found) ? found : null;
}
