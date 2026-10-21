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
    public const string WellDetailsPath = "Wells/Well/";
    // Avoid an unbounded request per well in large installations.
    private const int MaxDeviceDetailLookups = 25;
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
        ForeSiteWell[] wells;
        var session = new ForeSiteSession(client);
        try
        {
            using var response = await session.GetAsync(WellsPath, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Incomplete(await ExplainAsync(response, actingAs, cancellationToken), actingAs);
            }

            wells = await response.Content.ReadFromJsonAsync<ForeSiteWell[]>(JsonOptions, cancellationToken) ?? [];
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
            return Incomplete("ForeSite answered with something that is not a list of wells. Check the address.", actingAs);
        }

        var assetNames = await ReadAssetNamesAsync(session, cancellationToken);
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

        // Prefer the bulk response; a bounded detail fallback fills missing device types without
        // turning a large discovery into thousands of HTTP calls.
        var details = await ReadMissingDeviceTypesAsync(session, withId, warnings, cancellationToken);
        return new DiscoveryResult(true,
            withId.Select(well => ToDiscovered(well, assetNames, details.GetValueOrDefault(well.Id!.Value))).ToArray(),
            warnings, actingAs);
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

        public async Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct)
        {
            if (!_initialized) await CreateTokenAsync(ct);
            var response = await SendAsync(path, ct);
            if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
            response.Dispose();
            _cookies = new CookieContainer();
            _initialized = false;
            await CreateTokenAsync(ct);
            return await SendAsync(path, ct);
        }

        private async Task CreateTokenAsync(CancellationToken ct)
        {
            using var response = await SendAsync(TokenPath, ct);
            if (!response.IsSuccessStatusCode)
                throw new ForeSiteTokenException(await ExplainAsync(response, "the Gateway Windows account", ct));
            _initialized = true;
        }

        private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
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

    // Names only add context for grouping, so a failure here costs a warning, never the discovery.
    private static async Task<Dictionary<string, string>?> ReadAssetNamesAsync(ForeSiteSession session, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await session.GetAsync(CheckPath, cancellationToken);
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
        catch (ForeSiteTokenException)
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

    private static async Task<Dictionary<long, ForeSiteWell>> ReadMissingDeviceTypesAsync(
        ForeSiteSession session, ForeSiteWell[] wells, List<string> warnings, CancellationToken cancellationToken)
    {
        var missing = wells.Where(well => DeviceTypeName(well) is null).ToArray();
        var details = new Dictionary<long, ForeSiteWell>();
        var failures = 0;
        foreach (var well in missing.Take(MaxDeviceDetailLookups))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var id = well.Id!.Value;
                using var response = await session.GetAsync(WellDetailsPath + id.ToString(CultureInfo.InvariantCulture), cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    failures++;
                    continue;
                }

                var detail = await response.Content.ReadFromJsonAsync<ForeSiteWell>(JsonOptions, cancellationToken);
                if (detail is null || DeviceTypeName(detail) is null)
                {
                    failures++;
                    continue;
                }
                details.Add(id, detail);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failures++;
            }
            catch (HttpRequestException)
            {
                failures++;
            }
            catch (ForeSiteTokenException)
            {
                failures++;
            }
            catch (JsonException)
            {
                failures++;
            }
        }

        if (failures > 0)
            warnings.Add($"Device type details could not be read for {failures} well(s); those wells remain discoverable.");
        if (missing.Length > MaxDeviceDetailLookups)
            warnings.Add($"Device type detail lookup was limited to {MaxDeviceDetailLookups} wells; " +
                $"{missing.Length - MaxDeviceDetailLookups} additional well(s) have no discovered device type.");
        return details;
    }

    private static DiscoveredAsset ToDiscovered(
        ForeSiteWell well, Dictionary<string, string> assetNames, ForeSiteWell? detail)
    {
        var id = well.Id!.Value.ToString(CultureInfo.InvariantCulture);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["wellId"] = id };
        Put(metadata, "assetId", well.AssetId);
        if (metadata.TryGetValue("assetId", out var assetId) && assetNames.TryGetValue(assetId, out var assetName))
        {
            metadata["assetName"] = assetName;
        }

        if (!string.IsNullOrWhiteSpace(well.Name)) metadata["wellName"] = well.Name;
        AddDeviceFacts(metadata, well);
        // Bulk facts win; the per-well response only fills absent fields.
        if (detail is not null)
        {
            var detailFacts = new Dictionary<string, string>(StringComparer.Ordinal);
            AddDeviceFacts(detailFacts, detail);
            foreach (var fact in detailFacts) metadata.TryAdd(fact.Key, fact.Value);
        }
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

    private static void AddDeviceFacts(Dictionary<string, string> metadata, ForeSiteWell well)
    {
        var deviceType = DeviceTypeName(well);
        if (deviceType is not null) metadata["deviceType"] = deviceType;
        var typeId = Scalar(well.DeviceTypeId) ?? Member(well.PrincipalDeviceType, "Id") ?? Member(well.DeviceType, "Id");
        if (typeId is not null) metadata["deviceTypeId"] = typeId;
        var deviceName = Scalar(well.DeviceName) ?? Member(well.PrincipalDevice, "Name");
        if (deviceName is not null) metadata["deviceName"] = deviceName;
        var deviceId = Scalar(well.PrincipalDeviceId) ?? Member(well.PrincipalDevice, "Id");
        if (deviceId is not null) metadata["deviceId"] = deviceId;
    }

    private static string? DeviceTypeName(ForeSiteWell well) =>
        Scalar(well.PrincipalDeviceType) ?? Member(well.PrincipalDeviceType, "Name") ??
        Scalar(well.DeviceType) ?? Member(well.DeviceType, "Name");

    private static string? Scalar(JsonElement? value)
    {
        if (value is not { ValueKind: JsonValueKind.String or JsonValueKind.Number } element) return null;
        var text = element.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    private static string? Member(JsonElement? value, string member)
    {
        if (value is not { ValueKind: JsonValueKind.Object } element) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, member, StringComparison.OrdinalIgnoreCase))
                return Scalar(property.Value);
        }
        return null;
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
        JsonElement? Lease, JsonElement? FacilityId, JsonElement? PrincipalDeviceType,
        JsonElement? DeviceType = null, JsonElement? DeviceTypeId = null,
        JsonElement? PrincipalDevice = null, JsonElement? PrincipalDeviceId = null,
        JsonElement? DeviceName = null);
}
