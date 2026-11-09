using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>Existing typed current-read boundary over the same authenticated ForeSite session and parsers.</summary>
public sealed class ForeSiteCurrentDataProvider(ForeSiteSession session) : ICurrentDataProvider
{
    public async ValueTask<EngineeringValue> GetCurrentAsync(string assetExternalId, string quantity,
        CancellationToken cancellationToken = default) => quantity == ForeSiteCards.Quantity
        ? await ForeSiteCards.ReadAsync(session, assetExternalId, cancellationToken)
        : await ForeSiteMeasurements.ReadAsync(session, assetExternalId, quantity, cancellationToken);

    /// <summary>Read the two reviewed controller quantities from one native status document.</summary>
    public Task<IReadOnlyDictionary<string, EngineeringValue>> GetCurrentSnapshotAsync(
        string assetExternalId, IReadOnlyCollection<string> quantities,
        CancellationToken cancellationToken = default) =>
        ForeSiteSurveillanceSnapshot.ReadAsync(session, assetExternalId, quantities, cancellationToken);
}
