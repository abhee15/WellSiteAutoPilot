using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>Existing typed current-read boundary over the same authenticated ForeSite session and parsers.</summary>
public sealed class ForeSiteCurrentDataProvider(ForeSiteSession session) : ICurrentDataProvider
{
    public async ValueTask<EngineeringValue> GetCurrentAsync(string assetExternalId, string quantity,
        CancellationToken cancellationToken = default) => quantity switch
        {
            ForeSiteCards.Quantity => await ForeSiteCards.ReadAsync(session, assetExternalId, cancellationToken),
            // The two controller-scan quantities keep their own source; any other provider field is a production-day value.
            "PumpFillage" or "AveragePumpFillage" => await ForeSiteMeasurements.ReadAsync(session, assetExternalId, quantity, cancellationToken),
            _ => await ForeSiteLatestDailyValue.ReadAsync(session, assetExternalId, quantity, TimeProvider.System, cancellationToken)
        };

    /// <summary>Read the two reviewed controller quantities from one native status document.</summary>
    public Task<IReadOnlyDictionary<string, EngineeringValue>> GetCurrentSnapshotAsync(
        string assetExternalId, IReadOnlyCollection<string> quantities,
        CancellationToken cancellationToken = default) =>
        ForeSiteSurveillanceSnapshot.ReadAsync(session, assetExternalId, quantities, cancellationToken);
}
