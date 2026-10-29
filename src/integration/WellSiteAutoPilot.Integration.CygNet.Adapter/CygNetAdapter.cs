using System.Globalization;
using System.Net.Http;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>
/// What AutoPilot knows about a CygNet connection: a domain plus the site and service of one facility service
/// (CygNet writes that as SITE.SERVICE). This part has no CygNet libraries in it, so it builds and is tested everywhere;
/// the CygNet API itself sits behind <see cref="ICygNetFacilityReader"/> in builds that have the libraries.
/// Read-only: it never writes to CygNet.
/// </summary>
public static class CygNetAdapter
{
    public const string Key = "cygnet";
    public const string DomainSetting = "domain";
    public const string SiteSetting = "site";
    public const string ServiceSetting = "service";
    public const string ResourceKind = "cygnet.facility";

    /// <summary>Longest facility tag the current-data provider accepts; a longer discovered facility is not offered.</summary>
    public const int MaximumFacilityTagLength = 128;

    public static AdapterDescriptor Descriptor { get; } = new(
        Key, "CygNet",
        "Facilities held in one CygNet facility service. The Integration Gateway reads CygNet as the Windows account it runs as, " +
        "so that account needs a CygNet licence and access to the domain. Only builds of the Gateway that include the CygNet libraries can use this.",
        1,
        [
            new AdapterSettingField(DomainSetting, "Domain",
                "The CygNet domain number, for example 22567.", AdapterSettingKind.WholeNumber, true, 5),
            new AdapterSettingField(SiteSetting, "Site",
                "The CygNet site of the facility service, for example SITE1.", AdapterSettingKind.Text, true, 32),
            new AdapterSettingField(ServiceSetting, "Service",
                "The facility service on that site, for example UIS.", AdapterSettingKind.Text, true, 32)
        ],
        [new ProviderCapability(ProviderCapabilityKind.AssetDiscovery, "Facilities")],
        [new DiscoveryField("facilityType", "Facility type", "Unspecified type")]);

    /// <summary>Reads and checks the three settings. Returns null with a plain-words reason when they cannot be used.</summary>
    public static CygNetConnection? ReadConnection(IReadOnlyDictionary<string, string?> settings, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(settings);
        problem = null;
        settings.TryGetValue(DomainSetting, out var rawDomain);
        settings.TryGetValue(SiteSetting, out var rawSite);
        settings.TryGetValue(ServiceSetting, out var rawService);
        if (!int.TryParse(rawDomain?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var domain) || domain is < 0 or > 65535)
        {
            problem = "The domain must be a whole number from 0 to 65535.";
            return null;
        }
        var site = rawSite?.Trim();
        var service = rawService?.Trim();
        if (!IsName(site) || !IsName(service))
        {
            problem = "The site and service may use only letters, digits, underscores and hyphens.";
            return null;
        }
        return new CygNetConnection(domain, site!, service!);
    }

    private static bool IsName(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 32 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    /// <summary>Tries the connection: reaches the facility service. Failures are results, never exceptions.</summary>
    public static async Task<AdapterTestResult> CheckAsync(
        ICygNetFacilityReader reader, CygNetConnection connection, string actingAs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(actingAs);
        try
        {
            await reader.CheckAsync(connection, cancellationToken);
            return new(AdapterTestOutcomes.Healthy,
                $"CygNet answered and this account can reach {connection.SiteService} in domain {connection.Domain}.", actingAs);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new(AdapterTestOutcomes.Unreachable, "CygNet did not answer in time.", actingAs);
        }
        catch (CygNetUnavailableException exception)
        {
            return new(AdapterTestOutcomes.NeedsAttention, exception.Message, actingAs);
        }
        catch (UnauthorizedAccessException)
        {
            return new(AdapterTestOutcomes.NeedsAttention,
                "CygNet did not accept this account. Check its CygNet licence and access to the domain.", actingAs);
        }
        catch (HttpRequestException exception)
        {
            return new(AdapterTestOutcomes.Unreachable, "CygNet could not be reached: " + SafeVendorReason(exception), actingAs);
        }
        catch (Exception exception)
        {
            return new(AdapterTestOutcomes.NeedsAttention, "The CygNet check failed: " + SafeVendorReason(exception), actingAs);
        }
    }

    /// <summary>
    /// Lists the facilities of the connection's service. A failure is a result, not an exception: an incomplete answer with
    /// the reason, so nothing downstream treats it as "there are no facilities".
    /// </summary>
    public static async Task<DiscoveryResult> DiscoverAsync(
        ICygNetFacilityReader reader, CygNetConnection connection, string actingAs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(actingAs);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var facilities = await reader.ReadFacilitiesAsync(connection, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return MapFacilities(connection, facilities, actingAs);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Incomplete("CygNet did not answer before the connection timed out.", actingAs);
        }
        catch (CygNetUnavailableException exception)
        {
            return Incomplete(exception.Message, actingAs);
        }
        catch (UnauthorizedAccessException)
        {
            return Incomplete("CygNet did not accept this account. Check its CygNet licence and access to the domain.", actingAs);
        }
        catch (HttpRequestException exception)
        {
            return Incomplete("CygNet could not be reached: " + SafeVendorReason(exception), actingAs);
        }
        catch (Exception exception)
        {
            return Incomplete("CygNet facility discovery failed: " + SafeVendorReason(exception), actingAs);
        }
    }

    /// <summary>
    /// Turns what the reader found into discovered assets. The external id is the full facility tag (SITE.SERVICE::ID),
    /// which is what a CygNet read addresses, so a discovered facility can be read without translation. A facility that
    /// cannot be used is skipped with a warning and the result is marked incomplete; duplicate identities stop everything.
    /// </summary>
    public static DiscoveryResult MapFacilities(
        CygNetConnection connection, IReadOnlyCollection<CygNetFacilitySnapshot> facilities, string actingAs)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(facilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(actingAs);

        var warnings = new List<string>();
        var usable = new List<CygNetFacilitySnapshot>();
        foreach (var facility in facilities)
        {
            var id = facility.Id?.Trim();
            if (string.IsNullOrEmpty(id) || id.Any(char.IsControl) || id.Contains("::", StringComparison.Ordinal) ||
                connection.TagFor(id).Length > MaximumFacilityTagLength)
            {
                continue;
            }
            usable.Add(facility with { Id = id });
        }
        var skipped = facilities.Count - usable.Count;
        if (skipped > 0) warnings.Add($"{skipped} facility(ies) had no usable id and were skipped.");

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (usable.Any(facility => !identities.Add(facility.Id)))
            return Incomplete("CygNet returned duplicate facility identities. Nothing was imported.", actingAs);

        var mapped = usable
            .OrderBy(facility => facility.Id, StringComparer.OrdinalIgnoreCase)
            .Select(facility =>
            {
                var tag = connection.TagFor(facility.Id);
                var facts = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["domain"] = connection.Domain.ToString(CultureInfo.InvariantCulture),
                    ["site"] = connection.Site,
                    ["service"] = connection.Service,
                    ["facilityId"] = facility.Id,
                    ["facilityTag"] = tag
                };
                if (!string.IsNullOrWhiteSpace(facility.Description)) facts["description"] = facility.Description.Trim();
                if (!string.IsNullOrWhiteSpace(facility.Type)) facts["facilityType"] = facility.Type.Trim();
                return new DiscoveredAsset(tag, facility.Id, ResourceKind, null, facts);
            }).ToArray();

        return new DiscoveryResult(skipped == 0, mapped, warnings, actingAs);
    }

    /// <summary>
    /// What CygNet itself said, made safe to show: one line, no control characters, long token-like words and addresses
    /// removed, and short. Used only when the failure has no better explanation.
    /// </summary>
    public static string SafeVendorReason(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var words = (exception.Message ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length < 40 && !word.Contains("://", StringComparison.Ordinal))
            .Select(word => new string(word.Where(character => !char.IsControl(character)).ToArray()));
        var text = string.Join(' ', words).Trim();
        if (text.Length == 0) return "CygNet gave no reason.";
        return text.Length <= 200 ? text : text[..200] + "...";
    }

    private static DiscoveryResult Incomplete(string reason, string actingAs) => new(false, [], [reason], actingAs);
}

/// <summary>One CygNet facility service: a domain plus the site and service that hold the facilities.</summary>
public sealed record CygNetConnection(int Domain, string Site, string Service)
{
    /// <summary>CygNet's own spelling of the service, for example SITE1.UIS.</summary>
    public string SiteService => Site + "." + Service;

    /// <summary>The full tag of a facility in this service, for example SITE1.UIS::WELL0001.</summary>
    public string TagFor(string facilityId) => SiteService + "::" + facilityId;
}

/// <summary>A facility as the CygNet reader found it. Only the identity is required.</summary>
public sealed record CygNetFacilitySnapshot(string Id, string? Description = null, string? Type = null);

/// <summary>The CygNet libraries sit behind this seam, so everything above it is built and tested without them.</summary>
public interface ICygNetFacilityReader
{
    /// <summary>Reaches the facility service. Throws when it cannot; the adapter turns that into a plain-words result.</summary>
    Task CheckAsync(CygNetConnection connection, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CygNetFacilitySnapshot>> ReadFacilitiesAsync(
        CygNetConnection connection, CancellationToken cancellationToken);
}

/// <summary>A reason the connection cannot be used that is already worded for a person.</summary>
public sealed class CygNetUnavailableException(string message) : Exception(message);

/// <summary>Used by Gateway builds that do not include the CygNet libraries: every call says so, plainly.</summary>
public sealed class CygNetUnsupportedFacilityReader : ICygNetFacilityReader
{
    public const string Message =
        "This Integration Gateway was built without CygNet support. Build it on a CygNet machine with CYGNET_LIBRARY_PATH set to use CygNet connections.";

    public Task CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Task.FromException(new CygNetUnavailableException(Message));

    public Task<IReadOnlyCollection<CygNetFacilitySnapshot>> ReadFacilitiesAsync(
        CygNetConnection connection, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyCollection<CygNetFacilitySnapshot>>(new CygNetUnavailableException(Message));
}
