using System.Net;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>
/// What AutoPilot knows about ForeSite: how a connection to it is described and how it is checked. ForeSite
/// answers with the Windows identity of the caller, so every call here is made as the Gateway's own account.
/// </summary>
public static class ForeSiteAdapter
{
    public const string Key = "foresite";
    public const string BaseUrlSetting = "baseUrl";

    /// <summary>
    /// ForeSite has no health endpoint, and its Administration/ServiceStatus takes about 16 seconds and reports on
    /// services AutoPilot does not need. The asset list is a cheap read that needs the same permission discovery
    /// will, so a good answer from it means "reachable and allowed".
    /// </summary>
    public const string CheckPath = "SurfaceNetworkService/Asset/All";

    public static AdapterDescriptor Descriptor { get; } = new(
        Key,
        "ForeSite",
        "Weatherford ForeSite. Wells are read with the Windows account the Integration Gateway runs as, so that account needs a ForeSite login and the assets it should see.",
        1,
        [new AdapterSettingField(
            BaseUrlSetting, "ForeSite address",
            "The address that serves ForeSite's web services, for example http://foresite-server. ForeSite can take about 90 seconds to be ready after it starts.",
            AdapterSettingKind.Url, true)],
        []);

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
            return response.StatusCode switch
            {
                HttpStatusCode.OK => new(AdapterTestOutcomes.Healthy,
                    "ForeSite answered and allowed this account to read its assets.", actingAs),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(AdapterTestOutcomes.NeedsAttention,
                    $"ForeSite is reachable but did not accept {actingAs} (status {(int)response.StatusCode}). " +
                    "Give that account a ForeSite login with access to the assets it should see.", actingAs),
                HttpStatusCode.NotFound => new(AdapterTestOutcomes.NeedsAttention,
                    "Something answered at that address but it does not look like ForeSite's web services. Check the address.", actingAs),
                _ => new(AdapterTestOutcomes.NeedsAttention,
                    $"ForeSite answered with an error (status {(int)response.StatusCode}). " +
                    "If it has only just started, wait a few minutes and test again.", actingAs)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(AdapterTestOutcomes.Unreachable,
                "ForeSite did not answer in time. It can take about 90 seconds to be ready after it starts.", actingAs);
        }
        catch (HttpRequestException)
        {
            return new(AdapterTestOutcomes.Unreachable, "ForeSite could not be reached at that address.", actingAs);
        }
    }
}
