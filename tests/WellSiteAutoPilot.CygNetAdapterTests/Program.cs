using System.Net;
using System.Text;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet;

// Recorded-shape tests always run; the live check runs when CYGNET_AGENT_URL is set
// (plus CYGNET_DOMAIN / CYGNET_SITE / CYGNET_SERVICE), using the current Windows identity.
await RecordedResponseMapsFacilities();
await SettingsAreValidated();
await HealthStates();
await LiveDiscoveryIfConfigured();
Console.WriteLine("CygNet adapter tests passed.");

static async Task RecordedResponseMapsFacilities()
{
    const string body = """
        {"readFailed":true,"facilities":[
          {"site":"CYGQAUS","service":"UIS","siteService":"CYGQAUS.UIS","facilityId":"WELL0001","facilityType":"WELL","facilityDescription":"Pad A #1"},
          {"facilityId":"WELL0002"},
          {"facilityId":""}]}
        """;
    var handler = new StubHandler(HttpStatusCode.OK, body);
    var provider = Create(handler, new CygNetScope(27212, "CYGQAUS", "UIS"));

    var assets = await provider.DiscoverAssetsAsync();
    Assert(assets.Count == 2, "facilities without an id are skipped");
    Assert(handler.LastUri!.Contains("domain=27212") && handler.LastUri.Contains("site=CYGQAUS") &&
        handler.LastUri.Contains("service=UIS") && handler.LastUri.Contains("type=WELL"), "query carries the scope");
    var first = assets.First();
    Assert(first.ExternalId == "CYGQAUS.UIS::WELL0001" && first.DisplayName == "WELL0001", "fully qualified id");
    Assert(first.ProviderType == CygNetFacilityDiscoveryProvider.FacilityProviderType, "provider type");
    Assert(first.Metadata["description"] == "Pad A #1" && first.Metadata["facilityType"] == "WELL" &&
        first.Metadata["domain"] == "27212" && first.Metadata["siteService"] == "CYGQAUS.UIS", "metadata maps");
    var second = assets.Last();
    Assert(second.ExternalId == "CYGQAUS.UIS::WELL0002" && !second.Metadata.ContainsKey("description"),
        "scope fills missing site/service; blank description omitted");
    Assert((await provider.DiscoverAssetsAsync("next")).Count == 0, "no further pages");
    Assert((await provider.GetCompatibilityAsync()).State == ProviderCompatibilityState.Unknown,
        "compatibility is never claimed without evidence");
}

static Task SettingsAreValidated()
{
    var scope = CygNetScope.FromSettings(new Dictionary<string, string>
        { ["domain"] = "5", ["site"] = " S ", ["service"] = "UIS", ["facilityType"] = "PAD" });
    Assert(scope == new CygNetScope(5, "S", "UIS", "PAD"), "settings map and trim");
    Assert(CygNetScope.FromSettings(new Dictionary<string, string>
        { ["domain"] = "5", ["site"] = "S", ["service"] = "UIS" }).FacilityType == "WELL", "facility type defaults to WELL");
    foreach (var bad in new Dictionary<string, string>[]
    {
        new() { ["site"] = "S", ["service"] = "UIS" },
        new() { ["domain"] = "0", ["site"] = "S", ["service"] = "UIS" },
        new() { ["domain"] = "5", ["service"] = "UIS" }
    })
    {
        var threw = false;
        try { CygNetScope.FromSettings(bad); } catch (ArgumentException) { threw = true; }
        Assert(threw, "invalid settings are rejected");
    }
    return Task.CompletedTask;
}

static async Task HealthStates()
{
    var scope = new CygNetScope(1, "S", "UIS");
    Assert((await Create(new StubHandler(HttpStatusCode.OK, "{}"), scope).GetHealthAsync()).State == ProviderHealthState.Healthy, "any answer is healthy");
    Assert((await Create(new StubHandler(HttpStatusCode.Unauthorized, ""), scope).GetHealthAsync()).State == ProviderHealthState.Degraded, "401 degraded");
    Assert((await Create(new StubHandler(HttpStatusCode.InternalServerError, ""), scope).GetHealthAsync()).State == ProviderHealthState.Degraded, "agent up, CygNet down is degraded");
    Assert((await Create(new FailingHandler(), scope).GetHealthAsync()).State == ProviderHealthState.Unavailable, "unreachable agent");
}

static async Task LiveDiscoveryIfConfigured()
{
    var url = Environment.GetEnvironmentVariable("CYGNET_AGENT_URL");
    if (string.IsNullOrWhiteSpace(url))
    {
        Console.WriteLine("Live CygNet check skipped (CYGNET_AGENT_URL not set).");
        return;
    }
    var handler = new HttpClientHandler { UseDefaultCredentials = true };
    var scope = new CygNetScope(
        ushort.Parse(Environment.GetEnvironmentVariable("CYGNET_DOMAIN") ?? "1", System.Globalization.CultureInfo.InvariantCulture),
        Environment.GetEnvironmentVariable("CYGNET_SITE") ?? "SITE",
        Environment.GetEnvironmentVariable("CYGNET_SERVICE") ?? "UIS");
    var provider = new CygNetFacilityDiscoveryProvider(
        new HttpClient(handler) { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(60) }, scope);
    Console.WriteLine($"Live health: {(await provider.GetHealthAsync()).State}");
    var assets = await provider.DiscoverAssetsAsync();
    Console.WriteLine($"Live discovery: {assets.Count} facilities");
    foreach (var a in assets.Take(5)) Console.WriteLine($"  {a.ExternalId} | {a.Metadata.GetValueOrDefault("description")}");
}

static CygNetFacilityDiscoveryProvider Create(HttpMessageHandler handler, CygNetScope scope) =>
    new(new HttpClient(handler) { BaseAddress = new Uri("http://agent.test/") }, scope);

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
}

sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public string? LastUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastUri = request.RequestUri!.PathAndQuery;
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}

sealed class FailingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("connection refused");
}
