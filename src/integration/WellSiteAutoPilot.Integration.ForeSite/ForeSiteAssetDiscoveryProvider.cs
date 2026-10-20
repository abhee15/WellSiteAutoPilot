using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>Read-only ForeSite adapter: discovers wells through the WellService
/// REST endpoint (GET {base}/Wells/All) using the caller's Windows identity.</summary>
public sealed class ForeSiteAssetDiscoveryProvider(
    HttpClient httpClient,
    string providerId = "foresite") :
    IAssetDiscoveryProvider,
    IProviderHealthProvider,
    IProviderMetadataProvider
{
    public const string WellProviderType = "foresite.well";
    public const string TestedProfile = "ForeSite 5.7.1 (WellService /Wells/All)";

    // ForeSite's ServiceConnectionStatus: Unknown, Connected, Disconnected, ...
    private const int DisconnectedStatus = 2;

    // ForeSite's WellTypeId enum (stable numeric ids; r_WellType stores abbreviations only).
    private static readonly Dictionary<long, string> WellTypes =
        new()
        {
            [1] = "RRL", [2] = "ESP", [3] = "GInj", [4] = "GLift", [5] = "NF",
            [6] = "WInj", [7] = "WGInj", [8] = "PLift", [9] = "OT", [10] = "PCP",
            [12] = "SInj", [13] = "CInj"
        };

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

    public async ValueTask<IReadOnlyCollection<DiscoveredAsset>> DiscoverAssetsAsync(
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        // The service returns every well in one response; there is no paging to resume.
        if (!string.IsNullOrEmpty(continuationToken))
        {
            return [];
        }

        var wells = await GetWellsAsync(cancellationToken);
        return wells
            .Where(well => well.Id is not null)
            .Select(ToDiscoveredAsset)
            .ToArray();
    }

    public async ValueTask<ProviderHealth> GetHealthAsync(
        CancellationToken cancellationToken = default)
    {
        // Same probe the ForeSite Toolbox status screen uses: the server reports
        // the state of its own dependencies (WAMI, Catalog, CygNet sites, ...).
        try
        {
            using var response = await httpClient.GetAsync(
                "Administration/ServiceStatus", cancellationToken);
            response.EnsureSuccessStatusCode();
            var services = await response.Content.ReadFromJsonAsync<ForeSiteServiceStatus[]>(
                JsonOptions, cancellationToken) ?? [];
            var disconnected = services
                .Where(service => service.Status == DisconnectedStatus)
                .Select(service => service.Name ?? "unnamed")
                .ToArray();

            // Discovery only needs the server itself, so dependency outages degrade
            // rather than fail; the reason keeps them visible.
            return disconnected.Length == 0
                ? new ProviderHealth(
                    providerId, "ForeSite", ProviderHealthState.Healthy,
                    $"{services.Length} dependent service(s) connected.",
                    DateTimeOffset.UtcNow)
                : new ProviderHealth(
                    providerId, "ForeSite", ProviderHealthState.Degraded,
                    "Server reachable; dependent services disconnected: " +
                    string.Join(", ", disconnected) + ".",
                    DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is
            System.Net.HttpStatusCode.Unauthorized or
            System.Net.HttpStatusCode.Forbidden)
        {
            return new ProviderHealth(
                providerId, "ForeSite", ProviderHealthState.Degraded,
                $"Reachable but not authorized ({(int)ex.StatusCode}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new ProviderHealth(
                providerId, "ForeSite", ProviderHealthState.Unavailable, ex.Message);
        }
    }

    public ValueTask<ProviderDescriptor> GetDescriptorAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ProviderDescriptor(
            providerId,
            "ForeSite",
            "foresite",
            "0.1.0-poc",
            null,
            [new ProviderCapability(ProviderCapabilityKind.AssetDiscovery, "wells")]));

    public ValueTask<ProviderCompatibility> GetCompatibilityAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ProviderCompatibility(
            providerId,
            ProviderCompatibilityState.Unknown,
            "0.1.0-poc",
            null,
            $"ForeSite exposes no version endpoint; tested profile: {TestedProfile}."));

    private async Task<IReadOnlyCollection<ForeSiteWell>> GetWellsAsync(
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("Wells/All", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ForeSiteWell[]>(
            JsonOptions, cancellationToken) ?? [];
    }

    private static DiscoveredAsset ToDiscoveredAsset(ForeSiteWell well)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(metadata, "assetId", well.AssetId);
        Add(metadata, "wellTypeId", well.WellType);
        if (well.WellType is { ValueKind: JsonValueKind.Number } type &&
            type.TryGetInt64(out var typeId) &&
            WellTypes.TryGetValue(typeId, out var abbreviation))
        {
            metadata["wellType"] = abbreviation;
        }
        Add(metadata, "field", well.Field);
        Add(metadata, "lease", well.Lease);
        Add(metadata, "facilityId", well.FacilityId);

        return new DiscoveredAsset(
            well.Id!.Value.ToString(CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(well.Name)
                ? $"Well {well.Id}"
                : well.Name,
            WellProviderType,
            null,
            metadata);
    }

    // WellType is a numeric enum on the wire; unexpected shapes are skipped, not fatal.
    private static void Add(Dictionary<string, string> metadata, string key, JsonElement? value)
    {
        var text = value is { ValueKind: JsonValueKind.String or JsonValueKind.Number }
            ? value.Value.ToString()
            : null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            metadata[key] = text;
        }
    }

    // Only the fields the first discovery slice needs; the full WellDTO has ~70.
    private sealed record ForeSiteServiceStatus(string? Name, int Status);
    private sealed record ForeSiteWell(
        long? Id,
        string? Name,
        JsonElement? AssetId,
        JsonElement? WellType,
        JsonElement? Field,
        JsonElement? Lease,
        JsonElement? FacilityId);
}
