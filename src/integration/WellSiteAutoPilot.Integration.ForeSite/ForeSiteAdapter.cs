using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>
/// What AutoPilot knows about ForeSite: how a connection to it is described, checked and read. ForeSite answers
/// with the Windows identity of the caller, so every call here is made as the Gateway's own account.
/// </summary>
public static class ForeSiteAdapter
{
    public const string Key = "foresite";
    public const string BaseUrlSetting = "baseUrl";
    public const string WellResourceKind = "foresite.well";

    /// <summary>
    /// ForeSite has no health endpoint, and its Administration/ServiceStatus takes about 16 seconds and reports on
    /// services AutoPilot does not need. The asset list is a cheap read that needs the same permission discovery
    /// will, so a good answer from it means "reachable and allowed".
    /// </summary>
    public const string CheckPath = "SurfaceNetworkService/Asset/All";

    public const string WellsPath = "Wells/All";

    public static AdapterDescriptor Descriptor { get; } = new(
        Key,
        "ForeSite",
        "Weatherford ForeSite. Wells are read with the Windows account the Integration Gateway runs as, so that account needs a ForeSite login and the assets it should see.",
        1,
        [new AdapterSettingField(
            BaseUrlSetting, "ForeSite address",
            "The address that serves ForeSite's web services, for example http://foresite-server. ForeSite can take about 90 seconds to be ready after it starts.",
            AdapterSettingKind.Url, true)],
        [new ProviderCapability(ProviderCapabilityKind.AssetDiscovery, "Wells")],
        [
            new DiscoveryField("assetName", "ForeSite asset", "Unassigned", AsAttribute: false),
            new DiscoveryField("wellType", "Well type", "Unknown type"),
            new DiscoveryField("deviceType", "Device type", "Unspecified device"),
            new DiscoveryField("field", "Field", "Unspecified field"),
            new DiscoveryField("lease", "Lease", "Unspecified lease")
        ]);

    // ForeSite's WellTypeId enum: stable numeric ids on the wire, abbreviations for people.
    private static readonly Dictionary<long, string> WellTypes = new()
    {
        [1] = "RRL", [2] = "ESP", [3] = "GInj", [4] = "GLift", [5] = "NF",
        [6] = "WInj", [7] = "WGInj", [8] = "PLift", [9] = "OT", [10] = "PCP",
        [12] = "SInj", [13] = "CInj"
    };

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { NumberHandling = JsonNumberHandling.AllowReadingFromString };

    public static Uri BaseAddress(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        return new Uri(baseUrl.Trim().TrimEnd('/') + "/");
    }

    /// <summary>Tries the connection and says plainly what was found. Never includes anything ForeSite sent back.</summary>
    public static async Task<AdapterTestResult> CheckAsync(
        HttpClient client, string actingAs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        try
        {
            using var response = await client.GetAsync(CheckPath, cancellationToken);
            return response.StatusCode == HttpStatusCode.OK
                ? new(AdapterTestOutcomes.Healthy, "ForeSite answered and allowed this account to read its assets.", actingAs)
                : new(AdapterTestOutcomes.NeedsAttention, Explain(response.StatusCode, actingAs), actingAs);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(AdapterTestOutcomes.Unreachable, SlowMessage, actingAs);
        }
        catch (HttpRequestException)
        {
            return new(AdapterTestOutcomes.Unreachable, UnreachableMessage, actingAs);
        }
    }

    /// <summary>
    /// Lists the wells ForeSite lets this account see. A failure is a result, not an exception: an incomplete
    /// answer with the reason, so nothing downstream treats it as "there are no wells".
    /// </summary>
    public static async Task<DiscoveryResult> DiscoverAsync(
        HttpClient client, string actingAs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var warnings = new List<string>();
        ForeSiteWell[] wells;
        try
        {
            using var response = await client.GetAsync(WellsPath, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Incomplete(Explain(response.StatusCode, actingAs), actingAs);
            }

            wells = await response.Content.ReadFromJsonAsync<ForeSiteWell[]>(JsonOptions, cancellationToken) ?? [];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Incomplete(SlowMessage, actingAs);
        }
        catch (HttpRequestException)
        {
            return Incomplete(UnreachableMessage, actingAs);
        }
        catch (JsonException)
        {
            return Incomplete("ForeSite answered with something that is not a list of wells. Check the address.", actingAs);
        }

        var assetNames = await ReadAssetNamesAsync(client, cancellationToken);
        if (assetNames is null)
        {
            warnings.Add("ForeSite's asset names could not be read, so wells will not show which ForeSite asset they belong to.");
            assetNames = [];
        }

        var withId = wells.Where(well => well.Id is not null).ToArray();
        if (withId.Length < wells.Length)
        {
            warnings.Add($"{wells.Length - withId.Length} well(s) had no id and were skipped.");
        }

        return new DiscoveryResult(true, withId.Select(well => ToDiscovered(well, assetNames)).ToArray(), warnings, actingAs);
    }

    private const string SlowMessage =
        "ForeSite did not answer in time. It can take about 90 seconds to be ready after it starts.";
    private const string UnreachableMessage = "ForeSite could not be reached at that address.";

    private static DiscoveryResult Incomplete(string reason, string actingAs) => new(false, [], [reason], actingAs);

    private static string Explain(HttpStatusCode status, string actingAs) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            $"ForeSite is reachable but did not accept {actingAs} (status {(int)status}). " +
            "Give that account a ForeSite login with access to the assets it should see.",
        HttpStatusCode.NotFound =>
            "Something answered at that address but it does not look like ForeSite's web services. Check the address.",
        _ => $"ForeSite answered with an error (status {(int)status}). If it has only just started, wait a few minutes and try again."
    };

    // Names only add context for grouping, so a failure here costs a warning, never the discovery.
    private static async Task<Dictionary<string, string>?> ReadAssetNamesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(CheckPath, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var assets = await response.Content.ReadFromJsonAsync<ForeSiteAsset[]>(JsonOptions, cancellationToken) ?? [];
            return assets
                .Where(asset => asset.Id is not null && !string.IsNullOrWhiteSpace(asset.Name))
                .GroupBy(asset => asset.Id!.Value.ToString(CultureInfo.InvariantCulture))
                .ToDictionary(group => group.Key, group => group.First().Name!, StringComparer.Ordinal);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static DiscoveredAsset ToDiscovered(ForeSiteWell well, Dictionary<string, string> assetNames)
    {
        var id = well.Id!.Value.ToString(CultureInfo.InvariantCulture);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["wellId"] = id };
        Put(metadata, "assetId", well.AssetId);
        if (metadata.TryGetValue("assetId", out var assetId) && assetNames.TryGetValue(assetId, out var assetName))
        {
            metadata["assetName"] = assetName;
        }

        if (!string.IsNullOrWhiteSpace(well.Name)) metadata["wellName"] = well.Name;
        if (!string.IsNullOrWhiteSpace(well.PrincipalDeviceType)) metadata["deviceType"] = well.PrincipalDeviceType;
        Put(metadata, "wellTypeId", well.WellType);
        if (well.WellType is { ValueKind: JsonValueKind.Number } type && type.TryGetInt64(out var typeId) &&
            WellTypes.TryGetValue(typeId, out var abbreviation))
        {
            metadata["wellType"] = abbreviation;
        }

        Put(metadata, "field", well.Field);
        Put(metadata, "lease", well.Lease);
        Put(metadata, "facilityId", well.FacilityId);
        return new DiscoveredAsset(id, string.IsNullOrWhiteSpace(well.Name) ? $"Well {id}" : well.Name, WellResourceKind, null, metadata);
    }

    // Unexpected shapes are left out rather than fatal: ForeSite's WellDTO has about 70 fields and we read a few.
    private static void Put(Dictionary<string, string> metadata, string key, JsonElement? value)
    {
        var text = value is { ValueKind: JsonValueKind.String or JsonValueKind.Number } element ? element.ToString() : null;
        if (!string.IsNullOrWhiteSpace(text)) metadata[key] = text;
    }

    private sealed record ForeSiteAsset(long? Id, string? Name);

    private sealed record ForeSiteWell(
        long? Id, string? Name, JsonElement? AssetId, JsonElement? WellType, JsonElement? Field,
        JsonElement? Lease, JsonElement? FacilityId, string? PrincipalDeviceType);
}
