using System.Net;
using System.Text;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;

// Recorded-shape tests always run; the live check runs when FORESITE_BASE_URL is set
// (uses the current Windows identity, like the production backend identity would).
await RecordedResponseMapsWells();
await UnauthorizedReportsDegraded();
await UnreachableReportsUnavailable();
await LiveDiscoveryIfConfigured();
Console.WriteLine("ForeSite adapter tests passed.");

static async Task RecordedResponseMapsWells()
{
    const string body = """
        [{"Id":1,"Name":"Well_001","AssetId":77,"WellType":"Oil","Field":"F1","Lease":null,"Ignored":{"x":1}},
         {"Id":2,"Name":"","FacilityId":"FAC"},
         {"Name":"no id"}]
        """;
    var provider = Create(HttpStatusCode.OK, body);

    var assets = await provider.DiscoverAssetsAsync();
    Assert(assets.Count == 2, "wells without an id are skipped");
    var first = assets.First();
    Assert(first.ExternalId == "1" && first.DisplayName == "Well_001", "id and name map");
    Assert(first.ProviderType == ForeSiteAssetDiscoveryProvider.WellProviderType, "provider type");
    Assert(first.Metadata["assetId"] == "77" && first.Metadata["field"] == "F1", "metadata maps");
    Assert(!first.Metadata.ContainsKey("lease"), "null metadata omitted");
    Assert(assets.Last().DisplayName == "Well 2", "blank name falls back");
    Assert((await provider.DiscoverAssetsAsync("next")).Count == 0, "no further pages");
    Assert((await provider.GetHealthAsync()).State == ProviderHealthState.Healthy, "healthy");
    Assert((await provider.GetCompatibilityAsync()).State == ProviderCompatibilityState.Unknown,
        "compatibility is never claimed without evidence");
}

static async Task UnauthorizedReportsDegraded()
{
    var health = await Create(HttpStatusCode.Unauthorized, "").GetHealthAsync();
    Assert(health.State == ProviderHealthState.Degraded, "401 is degraded, not unavailable");
}

static async Task UnreachableReportsUnavailable()
{
    var provider = new ForeSiteAssetDiscoveryProvider(new HttpClient(new FailingHandler())
    {
        BaseAddress = new Uri("http://localhost:1/")
    });
    Assert((await provider.GetHealthAsync()).State == ProviderHealthState.Unavailable, "unavailable");
}

static async Task LiveDiscoveryIfConfigured()
{
    var url = Environment.GetEnvironmentVariable("FORESITE_BASE_URL");
    if (string.IsNullOrWhiteSpace(url))
    {
        Console.WriteLine("Live ForeSite check skipped (FORESITE_BASE_URL not set).");
        return;
    }

    using var handler = new HttpClientHandler { UseDefaultCredentials = true };
    using var client = new HttpClient(handler)
    {
        BaseAddress = new Uri(url.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(60)
    };
    var provider = new ForeSiteAssetDiscoveryProvider(client);

    var health = await provider.GetHealthAsync();
    Console.WriteLine($"Live health: {health.State} {health.Reason}");
    Assert(health.State == ProviderHealthState.Healthy, "live ForeSite is healthy");

    var assets = await provider.DiscoverAssetsAsync();
    foreach (var asset in assets)
    {
        Console.WriteLine($"  {asset.ExternalId} | {asset.DisplayName} | {string.Join(",", asset.Metadata)}");
    }

    Assert(assets.Count > 0, "live discovery returns wells");
}

static ForeSiteAssetDiscoveryProvider Create(HttpStatusCode status, string body) =>
    new(new HttpClient(new StubHandler(status, body)) { BaseAddress = new Uri("http://foresite.test/") });

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("Assertion failed: " + message);
    }
}

internal sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
}

internal sealed class FailingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("connection refused");
}
