using System.Globalization;
using System.Reflection;
using CygNet.API.Core;
using CygNet.API.Points;
using CygNet.Data.Core;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet;

/// <summary>Maps a logical quantity to a CygNet UDC on the facility, plus its engineering unit.</summary>
public sealed record CygNetQuantityMapping(string Udc, string Unit);

public sealed record CygNetProviderOptions(
    ushort? Domain,
    IReadOnlyDictionary<string, CygNetQuantityMapping> Quantities);

/// <summary>POC: reads current point values from CygNet through the managed CygNet.API assemblies already
/// installed on the host. Asset external id is a facility tag, <c>SITE.SERVICE::FACILITYID</c>
/// (for example <c>WSANET.UIS::WELL_001</c>). Quantity is looked up in the options map; an unmapped quantity
/// is treated as the UDC itself with no unit.</summary>
public sealed class CygNetCurrentDataProvider(CygNetProviderOptions options) : ICurrentDataProvider
{
    static CygNetCurrentDataProvider() => CygNetAssemblyResolver.Register();

    public ValueTask<EngineeringValue> GetCurrentAsync(
        string assetExternalId, string quantity, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(quantity);
        // The CygNet API is synchronous; keep it off the request thread.
        return new ValueTask<EngineeringValue>(Task.Run(() => Read(assetExternalId, quantity), cancellationToken));
    }

    private EngineeringValue Read(string assetExternalId, string quantity)
    {
        var mapping = options.Quantities.TryGetValue(quantity, out var found)
            ? found
            : new CygNetQuantityMapping(quantity, string.Empty);

        var facility = new FacilityTag(assetExternalId);
        var domain = options.Domain ?? new ServiceInformation().GetAmbientDomain();
        var uis = new DomainSiteService(domain, facility.SiteService);
        var pointTag = new PointTag(facility, mapping.Udc);

        var record = new RealtimeClient(uis).GetPointValueRecord(pointTag)
            ?? throw new InvalidOperationException($"CygNet returned no record for {pointTag}.");

        var raw = Convert.ToString(record.Value, CultureInfo.InvariantCulture);
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            throw new InvalidOperationException($"CygNet value '{raw}' for {pointTag} is not numeric.");

        // Unreliable data must never read as Good.
        var quality = record.Status.HasFlag(BaseStatusFlags.Unreliable) ? "Bad" : "Good";
        return new EngineeringValue(
            quantity, number, mapping.Unit, new DateTimeOffset(record.Timestamp.ToUniversalTime()), quality, "cygnet");
    }
}

/// <summary>CygNet loads its "*64" assemblies by name; they are not in deps.json, so resolve them from the app folder.</summary>
internal static class CygNetAssemblyResolver
{
    internal static void Register() =>
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
}
