using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>Optional typed historian capability. Absence means unsupported, never a current-value fallback.</summary>
public interface ICygNetHistoryClient
{
    Task<HistoricalReadResult> ReadHistoryAsync(CygNetConnection connection, string facilityId,
        HistoricalReadRequest request, CancellationToken cancellationToken);
}
