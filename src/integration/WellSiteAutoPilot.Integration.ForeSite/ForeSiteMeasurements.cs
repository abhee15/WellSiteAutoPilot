using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

public static class ForeSiteMeasurements
{
    /// <summary>The established scalar read now uses the shared, all-or-nothing snapshot parser.</summary>
    public static async Task<EngineeringValue> ReadAsync(ForeSiteSession session, string externalId, string quantity,
        CancellationToken cancellationToken)
    {
        var values = await ForeSiteSurveillanceSnapshot.ReadAsync(session, externalId, [quantity], cancellationToken);
        return values[quantity];
    }

    internal static ForeSiteSurveillanceQuantity Catalogued(string quantity) =>
        ForeSiteSurveillanceCatalogue.Find(quantity) ?? throw new ForeSiteMeasurementException("FORESITE_QUANTITY_UNSUPPORTED");

    internal static string SourceField(string quantity) => Catalogued(quantity).Field;
}

public sealed class ForeSiteMeasurementException(string code, string? detail = null) : Exception(detail ?? code)
{
    public string Code { get; } = code;
}
