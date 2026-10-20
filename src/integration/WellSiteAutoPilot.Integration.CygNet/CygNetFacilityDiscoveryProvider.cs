using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet;

/// <summary>Where to look in CygNet. Mirrors ForeSite's facility picker: domain + site + service
/// (the CygNet UIS/CVS service the wells are attached to) and the facility type that marks a well.</summary>
public sealed record CygNetScope(ushort Domain, string Site, string Service, string FacilityType = "WELL")
{
    public static CygNetScope FromSettings(IReadOnlyDictionary<string, string>? settings)
    {
        string Get(string key, string fallback = "") =>
            settings is not null && settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

        if (!ushort.TryParse(Get("domain"), out var domain) || domain == 0)
        {
            throw new ArgumentException("CygNet setting 'domain' must be a positive number.");
        }
        var site = Get("site");
        var service = Get("service");
        if (site.Length == 0 || service.Length == 0)
        {
            throw new ArgumentException("CygNet settings 'site' and 'service' are required.");
        }
        return new CygNetScope(domain, site, service, Get("facilityType", "WELL"));
    }
}

/// <summary>Read-only CygNet adapter. It never loads CygNet assemblies: it talks to the CygNet Native
/// Agent (a .NET Framework process on a machine where CygNet is installed) that reads the FAC service the same way
/// ForeSite does. Windows identity is passed through; CygNet ACS decides what each user may read.</summary>
public sealed class CygNetFacilityDiscoveryProvider(
    HttpClient agent,
    CygNetScope scope,
    string providerId = "cygnet") :
    IAssetDiscoveryProvider,
    IProviderHealthProvider,
    IProviderMetadataProvider
{
    public const string FacilityProviderType = "cygnet.facility";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public async ValueTask<IReadOnlyCollection<DiscoveredAsset>> DiscoverAssetsAsync(
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        // A FAC service query returns every facility in one response.
        if (!string.IsNullOrEmpty(continuationToken))
        {
            return [];
        }

        var url = $"facilities?domain={scope.Domain}&site={Uri.EscapeDataString(scope.Site)}" +
                  $"&service={Uri.EscapeDataString(scope.Service)}&type={Uri.EscapeDataString(scope.FacilityType)}";
        using var response = await agent.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"CygNet agent returned {(int)response.StatusCode}: {detail}", null, response.StatusCode);
        }
        var body = await response.Content.ReadFromJsonAsync<FacilityListResponse>(JsonOptions, cancellationToken);

        return (body?.Facilities ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f.FacilityId))
            .Select(ToDiscoveredAsset)
            .ToArray();
    }

    public async ValueTask<ProviderHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        // Same philosophy as ForeSite: any answer from the agent proves the path is up. A 5xx from the agent
        // means it is running but CygNet itself could not be reached, which is Degraded rather than Unavailable.
        try
        {
            using var response = await agent.GetAsync("health", cancellationToken);
            if ((int)response.StatusCode >= 500)
            {
                return new ProviderHealth(providerId, "CygNet", ProviderHealthState.Degraded,
                    $"CygNet agent reachable but reported {(int)response.StatusCode}.");
            }
            response.EnsureSuccessStatusCode();
            return new ProviderHealth(providerId, "CygNet", ProviderHealthState.Healthy,
                "CygNet agent responded.", DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            return new ProviderHealth(providerId, "CygNet", ProviderHealthState.Degraded,
                $"Reachable but not authorized ({(int)ex.StatusCode}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new ProviderHealth(providerId, "CygNet", ProviderHealthState.Unavailable, ex.Message);
        }
    }

    public ValueTask<ProviderDescriptor> GetDescriptorAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ProviderDescriptor(
            providerId, "CygNet", "cygnet", "0.1.0-poc", null,
            [new ProviderCapability(ProviderCapabilityKind.AssetDiscovery, "facilities")]));

    public ValueTask<ProviderCompatibility> GetCompatibilityAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ProviderCompatibility(
            providerId, ProviderCompatibilityState.Unknown, "0.1.0-poc", null,
            "CygNet version is not read by the POC agent; no compatibility is claimed."));

    private DiscoveredAsset ToDiscoveredAsset(AgentFacility f)
    {
        var site = string.IsNullOrWhiteSpace(f.Site) ? scope.Site : f.Site;
        var service = string.IsNullOrWhiteSpace(f.Service) ? scope.Service : f.Service;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["domain"] = scope.Domain.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["site"] = site,
            ["service"] = service,
            ["siteService"] = string.IsNullOrWhiteSpace(f.SiteService) ? $"{site}.{service}" : f.SiteService,
            ["facilityId"] = f.FacilityId!
        };
        if (!string.IsNullOrWhiteSpace(f.FacilityType)) metadata["facilityType"] = f.FacilityType;
        if (!string.IsNullOrWhiteSpace(f.FacilityDescription)) metadata["description"] = f.FacilityDescription;

        // Fully qualified id (site.service::facility) is unique across sites, like ForeSite's parent reference.
        return new DiscoveredAsset(
            $"{site}.{service}::{f.FacilityId}",
            f.FacilityId!,
            FacilityProviderType,
            null,
            metadata);
    }

    private sealed record FacilityListResponse(bool ReadFailed, AgentFacility[]? Facilities);

    private sealed record AgentFacility(
        string? Site, string? Service, string? SiteService,
        string? FacilityId, string? FacilityType, string? FacilityDescription);
}
