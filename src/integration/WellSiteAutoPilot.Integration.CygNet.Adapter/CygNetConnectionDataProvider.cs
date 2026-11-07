using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>Typed current-data boundary that retains the existing explicit Connection, scope and result checks.</summary>
public sealed class CygNetConnectionDataProvider(ICygNetClient client, CygNetConnection connection) : ICurrentDataProvider
{
    public async ValueTask<EngineeringValue> GetCurrentAsync(string assetExternalId, string quantity,
        CancellationToken cancellationToken = default) =>
        await CygNetAdapter.ReadCurrentAsync(client, connection, assetExternalId, quantity, cancellationToken);
}
