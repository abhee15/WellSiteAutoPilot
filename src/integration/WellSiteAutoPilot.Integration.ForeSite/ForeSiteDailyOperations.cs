using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>
/// One latest-production-day operation per catalogued daily quantity, so authors pick from the provider's list.
/// A field outside the list can still be declared by name and unit; the read verifies the unit.
/// </summary>
internal static class ForeSiteDailyOperations
{
    public static IReadOnlyList<AdapterOperationDescriptor> All { get; } =
        [.. ForeSiteDailyCatalogue.All.Select(Describe)];

    public static IReadOnlyList<string> Keys { get; } = [.. All.Select(operation => operation.Key)];

    private static AdapterOperationDescriptor Describe(ForeSiteDailyQuantity item) =>
        new AdapterOperationDescriptor("daily." + ForeSiteSurveillanceOperations.Kebab(item.Quantity), ForeSiteSurveillanceOperations.Label(item.Quantity) + " (latest production day)",
            "ForeSite value of the latest finished production day.",
            ProviderCapabilityKind.CurrentData, AdapterReadRules.EngineeringDailyCapability, "1.0",
            EngineeringOperationSchemas.CurrentRequest, EngineeringOperationSchemas.CurrentResponse,
            "Production-day end: the finished day the value belongs to, never the retrieval time.",
            "Daily records carry no verified quality; values are Uncertain.",
            new(1, MeasurementPayloadLimits.MaximumValueBytes, MeasurementPayloadLimits.MaximumReplyBytes, 10),
            item.Quantity, ForeSiteAdapter.WellResourceKind, item.Unit) with { Documentation = ForeSiteReference.Daily(item.Quantity, item.Unit) };
}
