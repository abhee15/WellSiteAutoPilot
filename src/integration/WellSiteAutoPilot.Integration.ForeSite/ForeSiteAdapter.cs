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
    /// Scope-first discovery. ForeSite's global Wells/All route cannot serve as an authorization boundary:
    /// it may return wells outside the Gateway account's accessible assets. No well is returned until the
    /// asset-filtered wells and control-group service contracts have been implemented and verified.
    /// A failed/incomplete response must never be interpreted as an empty complete inventory.
    /// </summary>
    public static async Task<DiscoveryResult> DiscoverAsync(
        HttpClient client, string actingAs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var session = new ForeSiteSession(client);
        try
        {
            using var response = await session.GetAsync(CheckPath, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Incomplete(await ExplainAsync(response, actingAs, cancellationToken), actingAs);

            var assets = await response.Content.ReadFromJsonAsync<ForeSiteAsset[]>(JsonOptions, cancellationToken);
            if (assets is null)
                return Incomplete("ForeSite did not return a valid accessible-asset list.", actingAs);

            // Only ForeSite's returned, accessible asset IDs may be passed to subsequent scoped reads.
            // In particular, 0 represents unassigned wells but must not be added unconditionally:
            // the service must explicitly include it in this account's accessible scope.
            var allowedAssetIds = assets.Where(asset => asset.Id is >= 0)
                .Select(asset => asset.Id!.Value)
                .Distinct()
                .ToArray();

            if (allowedAssetIds.Length == 0)
                return new DiscoveryResult(true, [], [], actingAs);

            // The concrete ForeSite endpoint and request DTO for fetching wells by asset-ID list,
            // and the control-group endpoint for device type, are not documented in this repo.
            // Do not fall back to Wells/All (global enumeration) or invent a service route.
            return Incomplete("ForeSite asset-scoped well discovery is not configured yet. " +
                "The asset-filtered well and control-group API contracts must be verified first.", actingAs);
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
            return Incomplete("ForeSite answered with something that is not a list of accessible assets.", actingAs);
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

    private sealed record ForeSiteAsset(long? Id, string? Name);
}
