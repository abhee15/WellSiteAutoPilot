using System.Globalization;
using System.Reflection;
using CygNet.API.Core;
using CygNet.API.Points;
using CygNet.Data.Core;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet;

/// <summary>Maps a logical quantity to a CygNet UDC on the facility, plus its engineering unit.</summary>
public sealed record CygNetQuantityMapping(string Udc, string Unit);

/// <summary>Gateway-wide settings. <see cref="Domain"/> null means the host's ambient CygNet domain.
/// Only quantities listed in <see cref="Quantities"/> can be read.</summary>
public sealed record CygNetProviderOptions(
    ushort? Domain,
    IReadOnlyDictionary<string, CygNetQuantityMapping> Quantities);

/// <summary>
/// Reads current point values from CygNet through the managed CygNet.API assemblies already installed on the host.
/// The asset external id is a facility tag, <c>SITE.SERVICE::FACILITYID</c> (for example <c>WSANET.UIS::WELL_001</c>).
/// Only quantities configured in <see cref="CygNetProviderOptions.Quantities"/> are read; an unlisted quantity is
/// refused rather than treated as a UDC name. Failures carry stable codes and a fixed safe sentence; whatever CygNet
/// said stays in the inner exception inside the Gateway.
/// </summary>
/// <remarks>
/// This is a development provider on the Gateway's <c>data/current</c> route. Execution reads of saved connections do
/// not use it: the worker refuses any non-simulator provider on that route.
/// </remarks>
public sealed class CygNetCurrentDataProvider(CygNetProviderOptions options) : ICurrentDataProvider
{
    private const int MaximumFacilityTagLength = 128;
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(10);

    static CygNetCurrentDataProvider() => CygNetAssemblyResolver.Register();

    public async ValueTask<EngineeringValue> GetCurrentAsync(
        string assetExternalId, string quantity, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(quantity);
        if (!options.Quantities.TryGetValue(quantity, out var mapping))
            throw Failure("CYGNET_QUANTITY_UNSUPPORTED", FailureKind.Validation,
                "This quantity is not configured for the CygNet provider.");
        if (!IsFacilityTag(assetExternalId))
            throw Failure("CYGNET_FACILITY_INVALID", FailureKind.Validation,
                "The asset external id is not a CygNet facility tag (SITE.SERVICE::FACILITYID).");
        try
        {
            // The CygNet API is synchronous and cannot be cancelled. Run it off the request thread and bound the wait
            // so a call that never returns cannot hold the request open.
            return await Task.Run(() => Read(assetExternalId, quantity, mapping), CancellationToken.None)
                .WaitAsync(ReadBudget, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw Failure("CYGNET_READ_TIMEOUT", FailureKind.DependencyTimeout,
                "CygNet did not return the value in time.");
        }
    }

    private EngineeringValue Read(string assetExternalId, string quantity, CygNetQuantityMapping mapping)
    {
        try
        {
            var facility = new FacilityTag(assetExternalId);
            var domain = options.Domain ?? new ServiceInformation().GetAmbientDomain();
            var uis = new DomainSiteService(domain, facility.SiteService);
            var pointTag = new PointTag(facility, mapping.Udc);

            var record = new RealtimeClient(uis).GetPointValueRecord(pointTag)
                ?? throw Failure("CYGNET_POINT_NOT_FOUND", FailureKind.DependencyUnavailable,
                    "CygNet returned no value for the point.");

            var raw = Convert.ToString(record.Value, CultureInfo.InvariantCulture);
            if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                throw Failure("CYGNET_VALUE_NOT_NUMERIC", FailureKind.DependencyUnavailable,
                    "CygNet returned a value that is not a number.");
            if (record.Timestamp == default)
                throw Failure("CYGNET_TIMESTAMP_INVALID", FailureKind.DependencyUnavailable,
                    "CygNet returned a value with no timestamp.");

            // Never let a flagged value read as Good: Unreliable is Bad; any other status flag is Uncertain, which a
            // module accepts only when it declares that it allows uncertain quality. Timestamp age is checked by the host.
            var quality = record.Status.HasFlag(BaseStatusFlags.Unreliable) ? "Bad"
                : IsClear(record.Status) ? "Good" : "Uncertain";
            return new EngineeringValue(
                quantity, number, mapping.Unit, new DateTimeOffset(record.Timestamp.ToUniversalTime()), quality, "cygnet",
                DateTimeOffset.UtcNow);
        }
        catch (WellSiteAutoPilotException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw Failure("CYGNET_READ_FAILED", FailureKind.DependencyUnavailable,
                "CygNet could not provide the value.", error);
        }
    }

    private static bool IsFacilityTag(string value) =>
        value.Length <= MaximumFacilityTagLength && value.Contains("::", StringComparison.Ordinal) &&
        !value.Any(char.IsControl);

    // Healthy points carry housekeeping flags (Initialized, Updated) and data-type bits; only another flag is a quality concern.
    // Matched by flag name so this holds whatever type the CygNet library uses for its status flags.
    private static readonly HashSet<string> NeutralFlags = new(StringComparer.OrdinalIgnoreCase)
        { "None", "0", "Initialized", "Updated", "StringData", "DigitalData", "OutputData" };

    private static bool IsClear<T>(T status) =>
        (status?.ToString() ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(NeutralFlags.Contains);

    private static WellSiteAutoPilotException Failure(string code, FailureKind kind, string detail, Exception? inner = null) =>
        new(code, kind, detail,
            retryable: kind is FailureKind.DependencyUnavailable or FailureKind.DependencyTimeout,
            innerException: inner);
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
