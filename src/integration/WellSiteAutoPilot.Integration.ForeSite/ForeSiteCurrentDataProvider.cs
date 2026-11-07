using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>Existing typed current-read boundary over the same authenticated ForeSite session and parsers.</summary>
public sealed class ForeSiteCurrentDataProvider(ForeSiteSession session) : ICurrentDataProvider
{
    public async ValueTask<EngineeringValue> GetCurrentAsync(string assetExternalId, string quantity,
        CancellationToken cancellationToken = default) => quantity == ForeSiteCards.Quantity
        ? await ForeSiteCards.ReadAsync(session, assetExternalId, cancellationToken)
        : await ForeSiteMeasurements.ReadAsync(session, assetExternalId, quantity, cancellationToken);
}
