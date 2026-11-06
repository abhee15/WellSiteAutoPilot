using System.Text.RegularExpressions;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>
/// Builds the quantity catalog of one CygNet facility from its points. CygNet has no list of available quantities, so a
/// quantity is available when its point exists. Keys are <c>cygnet.udc.&lt;UDC&gt;</c>; a mapping to the stable core keys
/// (PumpFillage and so on) stays a separate, optional step. The unit is the point's own unit, and an empty unit stays empty.
/// </summary>
public static partial class CygNetCatalog
{
    public const string KeyPrefix = "cygnet.udc.";

    [GeneratedRegex("^[A-Za-z0-9_]{1,16}$")]
    private static partial Regex UdcPattern();

    public static bool IsUdc(string? udc) => udc is not null && UdcPattern().IsMatch(udc);

    public static AssetQuantityCatalog Build(string facilityId, IReadOnlyList<CygNetPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var entries = points
            .Where(p => UdcPattern().IsMatch(p.Udc))
            .GroupBy(p => p.Udc.ToUpperInvariant(), StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(p => p.Udc, StringComparer.OrdinalIgnoreCase)
            .Select(p => new QuantityCatalogEntry(KeyPrefix + p.Udc.ToUpperInvariant(),
                string.IsNullOrWhiteSpace(p.Description) ? p.Udc : p.Description.Trim(), "current", p.Unit.Trim()))
            .ToList();
        // The device type needs the DDS service and is a separate gap (G13).
        return new(facilityId, null, null, entries);
    }

    public static async Task<AssetQuantityCatalog> ReadAsync(ICygNetClient client, CygNetConnection connection, string facilityId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(connection);
        return Build(facilityId, await client.ListPointsAsync(connection, facilityId, cancellationToken));
    }
}
