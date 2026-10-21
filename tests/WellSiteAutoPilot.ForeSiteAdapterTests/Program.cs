using System.Net;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;

// Recorded-shape checks always run. The live check runs when FORESITE_BASE_URL is set, as the current Windows
// identity, which is how the installed Gateway reaches ForeSite.
const string Account = @"CORP\svc-wsa";
const string Leaky = "SECRET-BODY-TEXT";

await Case(HttpStatusCode.OK, AdapterTestOutcomes.Healthy, "readable JSON");
await Case(HttpStatusCode.Unauthorized, AdapterTestOutcomes.NeedsAttention, Account);
await Case(HttpStatusCode.Forbidden, AdapterTestOutcomes.NeedsAttention, Account);
await Case(HttpStatusCode.NotFound, AdapterTestOutcomes.NeedsAttention, "does not look like ForeSite");
await Case(HttpStatusCode.InternalServerError, AdapterTestOutcomes.NeedsAttention, "wait a few minutes");
await Case(HttpStatusCode.ServiceUnavailable, AdapterTestOutcomes.NeedsAttention, "status 503");

var unreachable = await ForeSiteAdapter.CheckAsync(Client(new Failing(new HttpRequestException("no route"))), Account, CancellationToken.None);
Expect(unreachable.Outcome == AdapterTestOutcomes.Unreachable && unreachable.Message.Contains("could not be reached", StringComparison.Ordinal),
    "a refused connection must be Unreachable");
Expect(!unreachable.Message.Contains("no route", StringComparison.Ordinal), "exception text must not reach the person");

var slow = await ForeSiteAdapter.CheckAsync(Client(new Failing(new TaskCanceledException())), Account, CancellationToken.None);
Expect(slow.Outcome == AdapterTestOutcomes.Unreachable && slow.Message.Contains("90 seconds", StringComparison.Ordinal),
    "a timeout must be Unreachable and mention the warm-up");

using (var cancelled = new CancellationTokenSource())
{
    await cancelled.CancelAsync();
    try
    {
        await ForeSiteAdapter.CheckAsync(Client(new Failing(new TaskCanceledException())), Account, cancelled.Token);
        throw new InvalidOperationException("ForeSite adapter check failed: caller cancellation must not become a result.");
    }
    catch (OperationCanceledException)
    {
        // expected: the caller asked to stop
    }
}

var requested = new Recording();
await ForeSiteAdapter.CheckAsync(Client(requested), Account, CancellationToken.None);
Expect(requested.Paths.SequenceEqual(["/prefix/TokenService/CreateToken", "/prefix/SurfaceNetworkService/Asset/All"]) && requested.Method == HttpMethod.Get,
    "the check must be one read of the asset list under the connection's address");

Expect(ForeSiteAdapter.BaseAddress(" http://fs.example/prefix ").AbsoluteUri == "http://fs.example/prefix/",
    "the address must be trimmed and end with a slash");

var descriptor = ForeSiteAdapter.Descriptor;
Expect(descriptor.Key == "foresite" && descriptor.Fields.Count == 1 && descriptor.Fields[0].Key == ForeSiteAdapter.BaseUrlSetting &&
       descriptor.Fields[0].Kind == AdapterSettingKind.Url && descriptor.Fields[0].Required,
    "ForeSite is described by one required address");
Expect(AdapterSettingsValidator.Validate(descriptor, new Dictionary<string, string?> { ["baseUrl"] = "http://fs" }).Count == 0 &&
       AdapterSettingsValidator.Validate(descriptor, new Dictionary<string, string?>()).Count == 1,
    "the address is required and must be a URL");

await DiscoveryChecks();
await LiveIfConfigured();
Console.WriteLine("ForeSite adapter checks passed.");
return 0;

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("ForeSite adapter check failed: " + message + ".");
}

static HttpClient Client(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("http://foresite.test/prefix/") };

static async Task Case(HttpStatusCode status, string outcome, string messagePart)
{
    var result = await ForeSiteAdapter.CheckAsync(Client(new Stub(status)), Account, CancellationToken.None);
    Expect(result.Outcome == outcome, $"{(int)status} must be {outcome}, was {result.Outcome}");
    Expect(result.Message.Contains(messagePart, StringComparison.Ordinal), $"{(int)status} message lacked '{messagePart}': {result.Message}");
    Expect(result.ActingAs == Account, "the account used must be reported");
    Expect(!result.Message.Contains(Leaky, StringComparison.Ordinal), "ForeSite's response body must never reach the person");
}

static async Task DiscoveryChecks()
{
    // Sanitized ForeSite DTO shapes from poc/foresite-asset-discovery (WellFilterDTO and
    // WellConfig.GetWellAutonomousControlGroupData). Neither endpoint enumerates Wells/All.
    const string assets = """[{"Id":0,"Name":"Not Mapped"},{"Id":1,"Name":"North"},{"Id":2,"Name":"South"}]""";
    const string wells = """
        [{"Id":101,"Name":"Alpha 1","AssetId":1,"WellType":1,"Field":"Alpha","Lease":"L-1","FacilityId":"RPOC_0070"},
         {"Id":102,"Name":"Alpha 2","AssetId":2,"WellType":2,"PrincipalDeviceType":"RPC"},
         {"Id":103,"Name":"Loose 1","AssetId":null,"WellType":10},
         {"Id":104,"Name":"Alpha 4","AssetId":1,"WellType":1,"PrincipalDeviceType":"WFordKS"},
         {"AssetId":2,"Name":"No ID"}]
        """;
    const string devices = """
        {"Units":null,"Values":[
          {"WellId":101,"DeviceType":"LufkinSam","LogicEnabled":true},
          {"WellId":102,"DeviceType":"AEBEAM"},
          {"WellId":103,"DeviceType":null},
          {"WellId":104,"DeviceType":""}]}
        """;

    var routes = new ScopedRoutes(assets, wells, devices);
    var found = await ForeSiteAdapter.DiscoverAsync(Client(routes), Account, CancellationToken.None);
    Expect(found.Complete && found.ActingAs == Account && found.Items.Count == 4,
        "scoped discovery includes mapped/unmapped wells and skips entries without id");
    Expect(found.Warnings.Count == 1 && found.Warnings[0].Contains("valid id", StringComparison.Ordinal),
        "an invalid well id gets a safe warning");
    Expect(routes.Paths.SequenceEqual([
        "/prefix/TokenService/CreateToken", "/prefix/Wells/UserAssets",
        "/prefix/Wells/Well/Filter", "/prefix/WellConfig/GetWellAutonomousControlGroupData"
    ]), "discovery performs token, UserAssets and two scoped POSTs; never Wells/All");
    Expect(routes.Methods.SequenceEqual(["GET", "GET", "POST", "POST"]),
        "the ForeSite filtered wells and control-group operations require POST");
    Expect(routes.FilterBodies.Count == 2 &&
           routes.FilterBodies.All(body => body == "[0,1,2]"),
        "exact accessible asset scope including 0 is passed to both APIs");

    var first = found.Items.Single(item => item.ExternalId == "101");
    Expect(first.ProviderType == ForeSiteAdapter.WellResourceKind && first.DisplayName == "Alpha 1" &&
           first.Metadata["assetName"] == "North" && first.Metadata["assetId"] == "1" &&
           first.Metadata["wellType"] == "RRL" && first.Metadata["wellTypeId"] == "1" &&
           first.Metadata["deviceType"] == "LufkinSam" && first.Metadata["facilityId"] == "RPOC_0070" &&
           first.Metadata["field"] == "Alpha" && first.Metadata["lease"] == "L-1",
        "well details and autonomous-control device type are joined by WellId");
    var second = found.Items.Single(item => item.ExternalId == "102");
    Expect(second.Metadata["wellType"] == "ESP" && second.Metadata["deviceType"] == "AEBEAM",
        "control-group device type takes precedence over the WellDTO fallback");
    var unmapped = found.Items.Single(item => item.ExternalId == "103");
    Expect(unmapped.Metadata["assetName"] == "Not Mapped" && !unmapped.Metadata.ContainsKey("assetId") &&
           !unmapped.Metadata.ContainsKey("deviceType") && unmapped.Metadata["wellType"] == "PCP",
        "null AssetId belongs to explicitly authorized pseudo-asset 0 and blank device type stays unknown");
    Expect(found.Items.Single(item => item.ExternalId == "104").Metadata["deviceType"] == "WFordKS",
        "a populated PrincipalDeviceType is only the fallback when control-group device type is blank");

    var zeroOnly = new ScopedRoutes("""[{"Id":0,"Name":"Not Mapped"}]""",
        """[{"Id":50,"Name":"Loose","AssetId":null}]""", """{"Values":[]}""");
    var zeroResult = await ForeSiteAdapter.DiscoverAsync(Client(zeroOnly), Account, CancellationToken.None);
    Expect(zeroResult.Complete && zeroResult.Items.Single().ExternalId == "50" &&
           zeroOnly.FilterBodies.All(body => body == "[0]"), "0-only authorized scope is supported");

    var emptyRoutes = new ScopedRoutes("[]", wells, devices);
    var empty = await ForeSiteAdapter.DiscoverAsync(Client(emptyRoutes), Account, CancellationToken.None);
    Expect(empty.Complete && empty.Items.Count == 0 && emptyRoutes.Paths.Count == 2,
        "zero accessible assets returns no wells and never sends an empty filter (which can mean all wells)");

    var unauthorized = new ScopedRoutes("""[{"Id":1,"Name":"North"}]""",
        """[{"Id":99,"AssetId":2}]""", devices);
    var refusedScope = await ForeSiteAdapter.DiscoverAsync(Client(unauthorized), Account, CancellationToken.None);
    Expect(!refusedScope.Complete && refusedScope.Items.Count == 0 && unauthorized.FilterBodies.SequenceEqual(["[1]"]),
        "even an out-of-scope record returned by ForeSite is rejected before discovery");

    var unauthorizedUnmapped = new ScopedRoutes("""[{"Id":1,"Name":"North"}]""",
        """[{"Id":99,"AssetId":null}]""", devices);
    var rejectedUnmapped = await ForeSiteAdapter.DiscoverAsync(Client(unauthorizedUnmapped), Account, CancellationToken.None);
    Expect(!rejectedUnmapped.Complete && rejectedUnmapped.Items.Count == 0,
        "unmapped wells must not be included when UserAssets did not authorize 0");

    var noDevices = await ForeSiteAdapter.DiscoverAsync(
        Client(new ScopedRoutes(assets, wells, "{}", deviceStatus: HttpStatusCode.ServiceUnavailable)),
        Account, CancellationToken.None);
    Expect(noDevices.Complete && noDevices.Items.Count == 4 &&
           noDevices.Warnings.Any(item => item.Contains("control-group", StringComparison.Ordinal)) &&
           noDevices.Items.Single(item => item.ExternalId == "104").Metadata["deviceType"] == "WFordKS",
        "a failed optional device-type read warns but retains a complete scoped well inventory");

    var brokenWells = await ForeSiteAdapter.DiscoverAsync(
        Client(new ScopedRoutes(assets, wells, devices, wellStatus: HttpStatusCode.ServiceUnavailable)),
        Account, CancellationToken.None);
    Expect(!brokenWells.Complete && brokenWells.Items.Count == 0,
        "a failed scoped well filter never becomes an empty complete inventory");

    foreach (var malformed in new[] { "null", """[{"Name":"No ID"}]""", """[{"Id":-1}]""", "<html>broken</html>" })
    {
        var invalid = await ForeSiteAdapter.DiscoverAsync(
            Client(new ScopedRoutes(malformed, wells, devices)), Account, CancellationToken.None);
        Expect(!invalid.Complete && invalid.Items.Count == 0, "invalid accessible asset list must fail closed");
    }
    foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.NotFound, HttpStatusCode.InternalServerError })
    {
        var failed = await ForeSiteAdapter.DiscoverAsync(Client(new Stub(status)), Account, CancellationToken.None);
        Expect(!failed.Complete && failed.Items.Count == 0 && failed.Warnings.Count == 1 &&
               !failed.Warnings[0].Contains(Leaky, StringComparison.Ordinal),
            "non-success asset scope stays incomplete with no provider-body disclosure");
    }

    var unreachable = await ForeSiteAdapter.DiscoverAsync(
        Client(new Failing(new HttpRequestException("no route"))), Account, CancellationToken.None);
    Expect(!unreachable.Complete && !unreachable.Warnings[0].Contains("no route", StringComparison.Ordinal),
        "transport failures do not expose internal messages");

    await TokenAndRefreshChecks();
    Expect(ForeSiteAdapter.Descriptor.CanDiscover && ForeSiteAdapter.Descriptor.GroupingFields!.Select(item => item.Key)
        .SequenceEqual(["assetName", "wellType", "deviceType", "field", "lease"]),
        "ForeSite discovery exposes unchanged grouping fields");
}

static async Task TokenAndRefreshChecks()
{
    var flow = new TokenFlow();
    using var client = Client(flow);
    var result = await ForeSiteAdapter.CheckAsync(client, Account, CancellationToken.None);
    Expect(result.Outcome == AdapterTestOutcomes.Healthy, "refresh handshake should pass");
    Expect(flow.Paths.SequenceEqual([
        "/prefix/TokenService/CreateToken", "/prefix/SurfaceNetworkService/Asset/All",
        "/prefix/TokenService/CreateToken", "/prefix/SurfaceNetworkService/Asset/All"
    ]), "one 401 causes exactly one refreshed token and one retry");
    Expect(flow.Cookies.SequenceEqual(["", "session=first", "", "session=second"]),
        "renewed cookie is sent only on subsequent requests");
    var invalid = await ForeSiteAdapter.CheckAsync(Client(new ScopedRoutes("[]", "[]", """{"Values":[]}""", health: "<html>invalid</html>")), Account, CancellationToken.None);
    Expect(invalid.Outcome == AdapterTestOutcomes.NeedsAttention && invalid.Message.Contains("valid JSON", StringComparison.Ordinal),
        "HTML is not a healthy connection response");
    var problem = await ForeSiteAdapter.CheckAsync(Client(new ErrorFlow()), Account, CancellationToken.None);
    Expect(problem.Outcome == AdapterTestOutcomes.NeedsAttention &&
        problem.Message.Contains("Known ForeSite problem", StringComparison.Ordinal), "FullErrorMessage is reported");
}

static async Task LiveIfConfigured()
{
    var url = Environment.GetEnvironmentVariable("FORESITE_BASE_URL");
    if (string.IsNullOrWhiteSpace(url))
    {
        Console.WriteLine("Live ForeSite check skipped (FORESITE_BASE_URL not set).");
        return;
    }

    using var handler = new HttpClientHandler { UseDefaultCredentials = true };
    using var client = new HttpClient(handler) { BaseAddress = ForeSiteAdapter.BaseAddress(url), Timeout = TimeSpan.FromSeconds(60) };
    var result = await ForeSiteAdapter.CheckAsync(client, Environment.UserName, CancellationToken.None);
    Console.WriteLine($"Live ForeSite check: {result.Outcome} - {result.Message}");
    Expect(result.Outcome == AdapterTestOutcomes.Healthy, "the live ForeSite did not report Healthy");

    // Never run the expensive on-prem discovery in cloud CI or the ordinary live health probe.
    // An operator on the ForeSite network must opt in explicitly using the Gateway identity.
    if (Environment.GetEnvironmentVariable("FORESITE_DISCOVERY_SMOKE") == "1")
    {
        client.Timeout = TimeSpan.FromMinutes(5);
        var discovered = await ForeSiteAdapter.DiscoverAsync(client, Environment.UserName, CancellationToken.None);
        Console.WriteLine($"Live ForeSite scoped discovery: complete={discovered.Complete}, wells={discovered.Items.Count}, warnings={discovered.Warnings.Count}");
        Expect(discovered.Complete, "the live ForeSite scoped-well discovery was incomplete");
    }
}


internal sealed class TokenFlow : HttpMessageHandler
{
    public List<string> Paths { get; } = [];
    public List<string> Cookies { get; } = [];
    private int _tokenCount;
    private int _assetCount;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Paths.Add(path);
        Cookies.Add(request.Headers.TryGetValues("Cookie", out var cookies) ? string.Join(";", cookies) : "");
        if (path.EndsWith("/TokenService/CreateToken", StringComparison.Ordinal))
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("Set-Cookie", "session=" + (++_tokenCount == 1 ? "first" : "second") + "; Path=/");
            return Task.FromResult(response);
        }
        if (++_assetCount == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
        });
    }
}

internal sealed class ErrorFlow : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/TokenService/CreateToken", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"FullErrorMessage":"Known ForeSite problem"}""", System.Text.Encoding.UTF8, "application/json")
            });
}

internal sealed class Stub(HttpStatusCode status) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/TokenService/CreateToken", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.OK ? "[]" : "SECRET-BODY-TEXT") });
}

internal sealed class Failing(Exception failure) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested ? throw new OperationCanceledException(cancellationToken) : throw failure;
}

internal sealed class Recording : HttpMessageHandler
{
    public List<string> Paths { get; } = [];
    public HttpMethod? Method { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Paths.Add(request.RequestUri!.AbsolutePath);
        Method = request.Method;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
    }
}

internal sealed class ScopedRoutes(
    string assets, string wells, string devices,
    HttpStatusCode? wellStatus = null, HttpStatusCode? deviceStatus = null, string? health = null) : HttpMessageHandler
{
    public List<string> Paths { get; } = [];
    public List<string> Methods { get; } = [];
    public List<string> FilterBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Paths.Add(path);
        Methods.Add(request.Method.Method);
        if (path.EndsWith("/TokenService/CreateToken", StringComparison.Ordinal))
            return Json("{}");

        if (path.EndsWith("/SurfaceNetworkService/Asset/All", StringComparison.Ordinal))
            return Json(health ?? "[]");

        if (path.EndsWith("/Wells/UserAssets", StringComparison.Ordinal))
            return Json(assets);

        if (path.EndsWith("/Wells/Well/Filter", StringComparison.Ordinal) ||
            path.EndsWith("/WellConfig/GetWellAutonomousControlGroupData", StringComparison.Ordinal))
        {
            if (request.Method != HttpMethod.Post || request.Content is null)
                throw new InvalidOperationException("ForeSite scoped endpoints require POST with a JSON body.");
            using var doc = System.Text.Json.JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken));
            var ids = doc.RootElement.GetProperty("assetIds")
                .EnumerateArray().Select(item => item.GetInt64()).ToArray();
            FilterBodies.Add(System.Text.Json.JsonSerializer.Serialize(ids));

            if (path.EndsWith("/Wells/Well/Filter", StringComparison.Ordinal))
                return wellStatus is { } badWell ? new HttpResponseMessage(badWell) : Json(wells);
            return deviceStatus is { } badDevice ? new HttpResponseMessage(badDevice) : Json(devices);
        }

        // In particular, a request to Wells/All is never allowed.
        throw new InvalidOperationException("Unexpected ForeSite request: " + path);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
    };
}
