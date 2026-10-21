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
    public const string UserAssetsPath = "Wells/UserAssets";
    public const string WellsByAssetsPath = "Wells/GetWellsByUserAssetIds";
    public const string DeviceTypesPath = "WellConfig/GetWellAutonomousControlGroupData";

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
        var wells = new List<ForeSiteWell>();
        Dictionary<string, string>? assetNames;
        try
        {
            // The user's assets include a pseudo-asset "Not Mapped" (id 0) for wells that belong to no asset.
            // ForeSite's by-asset query returns nothing for id 0, so those wells come from the full list.
            using var assetsResponse = await client.GetAsync(UserAssetsPath, cancellationToken);
            if (!assetsResponse.IsSuccessStatusCode)
            {
                return Incomplete(Explain(assetsResponse.StatusCode, actingAs), actingAs);
            }

            var assets = await assetsResponse.Content.ReadFromJsonAsync<ForeSiteAsset[]>(JsonOptions, cancellationToken) ?? [];
            var assetIds = assets.Where(asset => asset.Id is > 0).Select(asset => asset.Id!.Value).Distinct().ToArray();
            var includesUnmapped = assets.Any(asset => asset.Id == 0);
            assetNames = assets
                .Where(asset => asset.Id is > 0 && !string.IsNullOrWhiteSpace(asset.Name))
                .GroupBy(asset => asset.Id!.Value.ToString(CultureInfo.InvariantCulture))
                .ToDictionary(group => group.Key, group => group.First().Name!, StringComparer.Ordinal);

            if (assetIds.Length > 0)
            {
                using var mapped = await client.PostAsJsonAsync(WellsByAssetsPath, assetIds, cancellationToken);
                if (!mapped.IsSuccessStatusCode)
                {
                    return Incomplete(Explain(mapped.StatusCode, actingAs), actingAs);
                }

                wells.AddRange(await mapped.Content.ReadFromJsonAsync<ForeSiteWell[]>(JsonOptions, cancellationToken) ?? []);
            }

            if (includesUnmapped)
            {
                using var all = await client.GetAsync(WellsPath, cancellationToken);
                if (!all.IsSuccessStatusCode)
                {
                    return Incomplete(Explain(all.StatusCode, actingAs), actingAs);
                }

                var everyWell = await all.Content.ReadFromJsonAsync<ForeSiteWell[]>(JsonOptions, cancellationToken) ?? [];
                wells.AddRange(everyWell.Where(well => IsUnmapped(well.AssetId)));
            }
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

        var withId = wells.Where(well => well.Id is not null).DistinctBy(well => well.Id).ToArray();
        var skipped = wells.Count(well => well.Id is null);
        if (skipped > 0)
        {
            warnings.Add($"{skipped} well(s) had no id and were skipped.");
        }

        // The well lists leave PrincipalDeviceType empty; the autonomous-control group data carries it for every well.
        var deviceTypes = await ReadDeviceTypesAsync(client, cancellationToken);
        if (deviceTypes is null)
        {
            warnings.Add("ForeSite's device types could not be read, so wells will not show their device type.");
            deviceTypes = [];
        }

        return new DiscoveryResult(
            true, withId.Select(well => ToDiscovered(well, assetNames, deviceTypes)).ToArray(), warnings, actingAs);
    }

    private static bool IsUnmapped(JsonElement? assetId) =>
        assetId is not { ValueKind: JsonValueKind.String or JsonValueKind.Number } element ||
        element.ToString() is "" or "0";

    private static async Task<Dictionary<string, string>?> ReadDeviceTypesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.PostAsJsonAsync(DeviceTypesPath, new { }, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var data = await response.Content.ReadFromJsonAsync<ForeSiteControlGroupData>(JsonOptions, cancellationToken);
            return (data?.Values ?? [])
                .Where(row => row.WellId is not null && !string.IsNullOrWhiteSpace(row.DeviceType))
                .GroupBy(row => row.WellId!.Value.ToString(CultureInfo.InvariantCulture))
                .ToDictionary(group => group.Key, group => group.First().DeviceType!, StringComparer.Ordinal);
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

    private static DiscoveredAsset ToDiscovered(ForeSiteWell well, Dictionary<string, string> assetNames, Dictionary<string, string> deviceTypes)
    {
        var id = well.Id!.Value.ToString(CultureInfo.InvariantCulture);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["wellId"] = id };
        Put(metadata, "assetId", well.AssetId);
        if (metadata.TryGetValue("assetId", out var assetId) && assetNames.TryGetValue(assetId, out var assetName))
        {
            metadata["assetName"] = assetName;
        }

        if (!string.IsNullOrWhiteSpace(well.Name)) metadata["wellName"] = well.Name;
        if (deviceTypes.TryGetValue(id, out var deviceType)) metadata["deviceType"] = deviceType;
        else if (!string.IsNullOrWhiteSpace(well.PrincipalDeviceType)) metadata["deviceType"] = well.PrincipalDeviceType;
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

    private sealed record ForeSiteControlGroupData(ForeSiteControlGroupRow[]? Values);

    private sealed record ForeSiteControlGroupRow(long? WellId, string? DeviceType);

    private sealed record ForeSiteWell(
        long? Id, string? Name, JsonElement? AssetId, JsonElement? WellType, JsonElement? Field,
        JsonElement? Lease, JsonElement? FacilityId, string? PrincipalDeviceType);
}
