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

    public const string TokenPath = "TokenService/CreateToken";
    public const string UserAssetsPath = "Wells/UserAssets";
    public const string WellsFilterPath = "Wells/Well/Filter";
    public const string DeviceTypesPath = "WellConfig/GetWellAutonomousControlGroupData";

    // Lift classification from ForeSite's WellTypeId, not the controller device type.
    private static readonly Dictionary<long, string> WellTypes = new()
    {
        [1] = "RRL", [2] = "ESP", [3] = "GInj", [4] = "GLift", [5] = "NF",
        [6] = "WInj", [7] = "WGInj", [8] = "PLift", [9] = "OT", [10] = "PCP",
        [12] = "SInj", [13] = "CInj"
    };

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
    /// Discover only wells selected by ForeSite's own user-asset filter. Wells/All is unscoped
    /// and must never be used as an access boundary or discovery fallback.
    /// </summary>
    public static async Task<DiscoveryResult> DiscoverAsync(
        HttpClient client, string actingAs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var session = new ForeSiteSession(client);
        try
        {
            using var assetResponse = await session.GetAsync(UserAssetsPath, cancellationToken);
            if (!assetResponse.IsSuccessStatusCode)
                return Incomplete(await ExplainAsync(assetResponse, actingAs, cancellationToken), actingAs);

            var assets = await assetResponse.Content.ReadFromJsonAsync<ForeSiteAsset[]>(JsonOptions, cancellationToken);
            if (assets is null || assets.Any(asset => asset is null || asset.Id is null or < 0))
                return Incomplete("ForeSite returned an invalid accessible asset list.", actingAs);

            // UserAssets includes pseudo-asset 0 only if ForeSite grants access to unmapped wells.
            // A blank AssetIds filter could mean ALL wells; never POST a blank filter.
            var ids = assets.Select(asset => asset.Id!.Value).Distinct().ToArray();
            if (ids.Length == 0)
                return new DiscoveryResult(true, [], [], actingAs);
            var allowedIds = ids.ToHashSet();
            var names = assets
                .Where(asset => !string.IsNullOrWhiteSpace(asset.Name))
                .GroupBy(asset => asset.Id!.Value)
                .ToDictionary(group => group.Key, group => group.First().Name!);
            var filter = new ForeSiteWellFilter(ids);

            using var wellResponse = await session.PostAsync(WellsFilterPath, filter, cancellationToken);
            if (!wellResponse.IsSuccessStatusCode)
                return Incomplete(await ExplainAsync(wellResponse, actingAs, cancellationToken), actingAs);
            var wells = await wellResponse.Content.ReadFromJsonAsync<ForeSiteWell[]>(JsonOptions, cancellationToken);
            if (wells is null)
                return Incomplete("ForeSite returned an invalid filtered well list.", actingAs);

            var matched = new List<(ForeSiteWell Well, long AssetId)>();
            var invalidWellIds = 0;
            foreach (var well in wells)
            {
                if (well is null || well.Id is null or <= 0)
                {
                    invalidWellIds++;
                    continue;
                }

                // A null WellDTO.AssetId represents ForeSite's Not Mapped asset (0).
                var assetId = ReadAssetId(well.AssetId);
                if (assetId is null || !allowedIds.Contains(assetId.Value))
                    return Incomplete("ForeSite returned wells outside this account's accessible assets.", actingAs);
                matched.Add((well, assetId.Value));
            }

            var warnings = new List<string>();
            if (invalidWellIds > 0)
                warnings.Add($"{invalidWellIds} well(s) had no valid id and were skipped.");
            var distinct = matched.DistinctBy(item => item.Well.Id).ToArray();
            if (distinct.Length < matched.Count)
                warnings.Add($"{matched.Count - distinct.Length} duplicate well(s) were skipped.");

            // Autonomous-control group classification is optional: a failure cannot expand scope,
            // and does not turn an otherwise complete inventory into a false "missing" result.
            var deviceTypes = await ReadDeviceTypesAsync(session, filter, warnings, cancellationToken);
            return new DiscoveryResult(true,
                distinct.Select(item => ToDiscovered(item.Well, item.AssetId, names, deviceTypes)).ToArray(),
                warnings, actingAs);
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
            return Incomplete("ForeSite returned an invalid discovery response.", actingAs);
        }
    }

    private static long? ReadAssetId(JsonElement? value)
    {
        if (value is null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
            return 0;
        if (value is not { ValueKind: JsonValueKind.Number or JsonValueKind.String } scalar)
            return null;
        return long.TryParse(scalar.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id >= 0
            ? id : null;
    }

    private static async Task<Dictionary<long, string>> ReadDeviceTypesAsync(
        ForeSiteSession session, ForeSiteWellFilter filter, List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await session.PostAsync(DeviceTypesPath, filter, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var group = await response.Content.ReadFromJsonAsync<ForeSiteControlGroupData>(JsonOptions, cancellationToken);
                if (group?.Values is not null)
                {
                    var rows = group.Values
                        .Where(row => row is not null && row.WellId is >= 0 && !string.IsNullOrWhiteSpace(row.DeviceType))
                        .GroupBy(row => row.WellId!.Value).ToArray();
                    var conflicting = rows.Where(groupRows =>
                        groupRows.Select(row => row.DeviceType!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
                        .Select(groupRows => groupRows.Key).ToHashSet();
                    if (conflicting.Count > 0)
                        warnings.Add($"{conflicting.Count} well(s) returned conflicting device-type records.");
                    return rows.Where(groupRows => !conflicting.Contains(groupRows.Key))
                        .ToDictionary(groupRows => groupRows.Key, groupRows => groupRows.First().DeviceType!.Trim());
                }
            }
        }
        catch (HttpRequestException) { }
        catch (JsonException) { }
        catch (ForeSiteTokenException) { }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        warnings.Add("ForeSite control-group device types could not be read; wells remain discoverable.");
        return [];
    }

    private static DiscoveredAsset ToDiscovered(
        ForeSiteWell well, long assetId, Dictionary<long, string> names, Dictionary<long, string> types)
    {
        var id = well.Id!.Value.ToString(CultureInfo.InvariantCulture);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["wellId"] = id };
        if (assetId > 0) metadata["assetId"] = assetId.ToString(CultureInfo.InvariantCulture);
        if (names.TryGetValue(assetId, out var name)) metadata["assetName"] = name;
        else if (assetId == 0) metadata["assetName"] = "Not Mapped";
        if (!string.IsNullOrWhiteSpace(well.Name)) metadata["wellName"] = well.Name;
        if (types.TryGetValue(well.Id.Value, out var type)) metadata["deviceType"] = type;
        else if (!string.IsNullOrWhiteSpace(well.PrincipalDeviceType)) metadata["deviceType"] = well.PrincipalDeviceType.Trim();
        Put(metadata, "wellTypeId", well.WellType);
        if (well.WellType is { ValueKind: JsonValueKind.Number or JsonValueKind.String } wellType &&
            long.TryParse(wellType.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var typeId) &&
            WellTypes.TryGetValue(typeId, out var wellTypeName))
            metadata["wellType"] = wellTypeName;
        Put(metadata, "field", well.Field);
        Put(metadata, "lease", well.Lease);
        Put(metadata, "facilityId", well.FacilityId);
        return new DiscoveredAsset(id,
            string.IsNullOrWhiteSpace(well.Name) ? $"Well {id}" : well.Name,
            WellResourceKind, null, metadata);
    }

    private static void Put(Dictionary<string, string> metadata, string key, JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.String or JsonValueKind.Number } value) return;
        var text = value.ToString();
        if (!string.IsNullOrWhiteSpace(text)) metadata[key] = text;
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

    /// <summary>Per-operation cookie jar; Windows auth remains attached by HttpClient's handler.</summary>
    private sealed class ForeSiteSession(HttpClient client)
    {
        private CookieContainer _cookies = new();
        private bool _initialized;

        public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct) =>
            SendAuthenticatedAsync(HttpMethod.Get, path, null, ct);

        public Task<HttpResponseMessage> PostAsync(string path, ForeSiteWellFilter filter, CancellationToken ct) =>
            SendAuthenticatedAsync(HttpMethod.Post, path, filter, ct);

        private async Task<HttpResponseMessage> SendAuthenticatedAsync(
            HttpMethod method, string path, ForeSiteWellFilter? body, CancellationToken ct)
        {
            if (!_initialized) await CreateTokenAsync(ct);
            var response = await SendAsync(method, path, body, ct);
            if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
            response.Dispose();
            _cookies = new CookieContainer();
            _initialized = false;
            await CreateTokenAsync(ct);
            return await SendAsync(method, path, body, ct);
        }

        private async Task CreateTokenAsync(CancellationToken ct)
        {
            using var response = await SendAsync(HttpMethod.Get, TokenPath, null, ct);
            if (!response.IsSuccessStatusCode)
                throw new ForeSiteTokenException(await ExplainAsync(response, "the Gateway Windows account", ct));
            _initialized = true;
        }

        private async Task<HttpResponseMessage> SendAsync(
            HttpMethod method, string path, ForeSiteWellFilter? body, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
            var uri = new Uri(client.BaseAddress ?? throw new InvalidOperationException("ForeSite requires a base address."), path);
            var cookies = _cookies.GetCookieHeader(uri);
            if (!string.IsNullOrEmpty(cookies)) request.Headers.TryAddWithoutValidation("Cookie", cookies);
            var response = await client.SendAsync(request, ct);
            if (response.Headers.TryGetValues("Set-Cookie", out var headers))
            {
                try
                {
                    foreach (var header in headers) _cookies.SetCookies(uri, header);
                }
                catch (CookieException)
                {
                    response.Dispose();
                    throw new ForeSiteTokenException("ForeSite returned an invalid session cookie.");
                }
            }
            return response;
        }
    }

    private sealed class ForeSiteTokenException(string message) : Exception(message) { }

    private static async Task<string> ExplainAsync(HttpResponseMessage response, string actingAs, CancellationToken ct)
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

    private sealed record ForeSiteAsset(long? Id, string? Name);
    private sealed record ForeSiteWellFilter(long[] AssetIds);
    private sealed record ForeSiteControlGroupData(ForeSiteControlGroupRow[]? Values);
    private sealed record ForeSiteControlGroupRow(long? WellId, string? DeviceType);
    private sealed record ForeSiteWell(
        long? Id, string? Name, JsonElement? AssetId, JsonElement? WellType, JsonElement? Field,
        JsonElement? Lease, JsonElement? FacilityId, string? PrincipalDeviceType);
}
