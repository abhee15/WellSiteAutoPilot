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

    public const string UserAssetsPath = "Wells/UserAssets";
    public const string WellsFilterPath = "Wells/Well/Filter";
    public const string DeviceTypesPath = "WellConfig/GetWellAutonomousControlGroupData";
    public const string TokenPath = "TokenService/CreateToken";

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
            new DiscoveryField("assetName", "ForeSite asset", "Unassigned", AsAttribute: false, IdentityMetadataKey: "assetId"),
            new DiscoveryField("wellType", "Well type", "Unknown type", IdentityMetadataKey: "wellTypeId"),
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

    /// <summary>Token handshake followed by JSON validation as the Gateway Windows account.</summary>
    public static async Task<AdapterTestResult> CheckAsync(HttpClient client, string actingAs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        try
        {
            using var response = await new ForeSiteSession(client).GetAsync(CheckPath, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(AdapterTestOutcomes.NeedsAttention, await ExplainAsync(response, actingAs, cancellationToken), actingAs);
            try
            {
                using var json = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                return json.RootElement.ValueKind == JsonValueKind.Array
                    ? new(AdapterTestOutcomes.Healthy, "ForeSite returned a readable JSON asset list.", actingAs)
                    : new(AdapterTestOutcomes.NeedsAttention, "ForeSite returned JSON but not an asset list.", actingAs);
            }
            catch (JsonException)
            {
                return new(AdapterTestOutcomes.NeedsAttention, "ForeSite did not return valid JSON.", actingAs);
            }
        }
        catch (ForeSiteTokenException error)
        {
            return new(AdapterTestOutcomes.NeedsAttention, error.Message, actingAs);
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
        var session = new ForeSiteSession(client);
        try
        {
            // Never use Wells/All: it bypasses ForeSite's user/asset filtering. A nonempty filter,
            // including Not Mapped (0), is also required when this account has no mapped assets.
            using var assetsResponse = await session.GetAsync(UserAssetsPath, cancellationToken);
            if (!assetsResponse.IsSuccessStatusCode)
                return Incomplete(await ExplainAsync(assetsResponse, actingAs, cancellationToken), actingAs);
            var assets = await assetsResponse.Content.ReadFromJsonAsync<ForeSiteAsset[]>(JsonOptions, cancellationToken)
                ?? throw new JsonException("Missing asset list.");
            if (assets.Any(asset => asset is null || asset.Id is null or < 0))
                throw new JsonException("Invalid asset identity.");
            var scope = new ForeSiteFilter(assets.Select(asset => asset.Id!.Value).Append(0L).Distinct().Order().ToArray());
            var allowed = scope.AssetIds.ToHashSet();
            var assetNames = assets.Where(asset => !string.IsNullOrWhiteSpace(asset.Name))
                .GroupBy(asset => asset.Id!.Value.ToString(CultureInfo.InvariantCulture))
                .ToDictionary(group => group.Key, group => group.First().Name!, StringComparer.Ordinal);

            using var response = await session.PostAsync(WellsFilterPath, scope, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Incomplete(await ExplainAsync(response, actingAs, cancellationToken), actingAs);
            var wells = await response.Content.ReadFromJsonAsync<ForeSiteWell[]>(JsonOptions, cancellationToken)
                ?? throw new JsonException("Missing well list.");
            if (wells.Any(well => well is null)) throw new JsonException("Invalid well row.");
            // Refuse an incorrectly scoped provider response instead of importing wells this filter did not request.
            if (wells.Any(well => !allowed.Contains(AssetId(well.AssetId))))
                return Incomplete("ForeSite returned wells outside the requested asset scope. No wells were accepted.", actingAs);
            var withId = wells.Where(well => well.Id is not null).DistinctBy(well => well.Id).ToArray();
            var skipped = wells.Count(well => well.Id is null);
            if (skipped > 0) warnings.Add($"{skipped} well(s) had no id and were skipped.");
            var beforeDeviceWarnings = warnings.Count;
            var deviceTypes = withId.Length == 0 ? new Dictionary<string, string>(StringComparer.Ordinal)
                : await ReadDeviceTypesAsync(session, scope, actingAs, warnings, cancellationToken);
            return new DiscoveryResult(skipped == 0, withId.Select(well => ToDiscovered(well, assetNames, deviceTypes)).ToArray(), warnings, actingAs,
                warnings.Count > beforeDeviceWarnings ? ["deviceType"] : [],
                scope.AssetIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToArray());
        }
        catch (ForeSiteTokenException error)
        {
            return Incomplete(error.Message, actingAs);
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
            return Incomplete("ForeSite answered with something that is not a valid asset or well list. Check the address.", actingAs);
        }
    }

    private static long AssetId(JsonElement? value) => value is null or { ValueKind: JsonValueKind.Null } ? 0
        : TryNumber(value.Value, out var id) && id >= 0 ? id : throw new JsonException("Invalid well asset identity.");

    private static bool TryNumber(JsonElement value, out long number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number ? value.TryGetInt64(out number)
            : value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
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

    internal static async Task<string> ExplainAsync(HttpResponseMessage response, string actingAs, CancellationToken ct)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("FullErrorMessage", out var detail) &&
                detail.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(detail.GetString()))
            {
                var message = detail.GetString()!;
                // Avoid echoing excessively long server-provided text in the UI.
                return message.Length > 1024 ? message[..1024] : message;
            }
        }
        catch (JsonException) { }
        return Explain(response.StatusCode, actingAs);
    }

    private static async Task<Dictionary<string, string>> ReadDeviceTypesAsync(ForeSiteSession session,
        ForeSiteFilter scope, string actingAs, List<string> warnings, CancellationToken cancellationToken)
    {
        string reason;
        try
        {
            using var response = await session.PostAsync(DeviceTypesPath, scope, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<ForeSiteControlGroupData>(JsonOptions, cancellationToken);
                if (data?.Values is null || data.Values.Any(row => row is null)) throw new JsonException("Missing device rows.");
                return data.Values.Where(row => row.WellId is not null && !string.IsNullOrWhiteSpace(row.DeviceType))
                    .GroupBy(row => row.WellId!.Value.ToString(CultureInfo.InvariantCulture))
                    .ToDictionary(group => group.Key, group => group.First().DeviceType!, StringComparer.Ordinal);
            }
            reason = await ExplainAsync(response, actingAs, cancellationToken);
        }
        catch (HttpRequestException) { reason = UnreachableMessage; }
        catch (ForeSiteTokenException error) { reason = error.Message; }
        catch (JsonException) { reason = "ForeSite returned invalid device-type data."; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { reason = SlowMessage; }
        warnings.Add("ForeSite's device types could not be read. " + reason);
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static DiscoveredAsset ToDiscovered(ForeSiteWell well, Dictionary<string, string> assetNames, Dictionary<string, string> deviceTypes)
    {
        var id = well.Id!.Value.ToString(CultureInfo.InvariantCulture);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["wellId"] = id };
        metadata["assetId"] = AssetId(well.AssetId).ToString(CultureInfo.InvariantCulture);
        if (assetNames.TryGetValue(AssetId(well.AssetId).ToString(CultureInfo.InvariantCulture), out var assetName))
        {
            metadata["assetName"] = assetName;
        }

        if (!string.IsNullOrWhiteSpace(well.Name)) metadata["wellName"] = well.Name;
        if (deviceTypes.TryGetValue(id, out var deviceType)) metadata["deviceType"] = deviceType;
        else if (!string.IsNullOrWhiteSpace(well.PrincipalDeviceType)) metadata["deviceType"] = well.PrincipalDeviceType;
        if (well.WellType is { } numericType && TryNumber(numericType, out var normalizedTypeId))
            metadata["wellTypeId"] = normalizedTypeId.ToString(CultureInfo.InvariantCulture);
        else Put(metadata, "wellTypeId", well.WellType);
        if (well.WellType is { } type && TryNumber(type, out var typeId) &&
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

    private sealed record ForeSiteFilter(long[] AssetIds);
    private sealed record ForeSiteControlGroupData(ForeSiteControlGroupRow[]? Values);
    private sealed record ForeSiteControlGroupRow(long? WellId, string? DeviceType);

    private sealed record ForeSiteAsset(long? Id, string? Name);

    private sealed record ForeSiteWell(
        long? Id, string? Name, JsonElement? AssetId, JsonElement? WellType, JsonElement? Field,
        JsonElement? Lease, JsonElement? FacilityId, string? PrincipalDeviceType);
}
