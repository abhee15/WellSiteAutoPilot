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
    const string wells = """
        [{"id":101,"name":"Alpha 1","assetId":1,"wellType":1,"field":"Alpha","lease":"L-1","facilityId":7,"principalDeviceType":"RPC","extra":{"ignored":true}},
         {"id":"102","name":"Alpha 2","assetId":2,"wellType":2,"principalDeviceType":null},
         {"id":103,"name":null,"assetId":99,"wellType":77},
         {"name":"No id"}]
        """;
    const string assets = """[{"id":1,"name":"North"},{"id":2,"name":"South"}]""";

    var found = await ForeSiteAdapter.DiscoverAsync(Client(new Routes(wells, assets)), Account, CancellationToken.None);
    Expect(found.Complete && found.ActingAs == Account, "a good answer is complete and names the account");
    Expect(found.Items.Count == 3, "wells without an id are skipped");
    Expect(found.Warnings.Any(item => item.Contains("had no id", StringComparison.Ordinal)), "a skipped well is reported");
    Expect(found.Warnings.Any(item => item.Contains("Device type details", StringComparison.Ordinal)),
        "failed optional detail reads report a warning without hiding the wells");
    var first = found.Items.Single(item => item.ExternalId == "101");
    Expect(first.DisplayName == "Alpha 1" && first.ProviderType == ForeSiteAdapter.WellResourceKind && first.ParentExternalId is null,
        "name, kind and no parent");
    Expect(first.Metadata["assetName"] == "North" && first.Metadata["wellType"] == "RRL" && first.Metadata["deviceType"] == "RPC" &&
           first.Metadata["field"] == "Alpha" && first.Metadata["lease"] == "L-1" && first.Metadata["facilityId"] == "7" &&
           first.Metadata["assetId"] == "1" && first.Metadata["wellTypeId"] == "1" && first.Metadata["wellId"] == "101" &&
           first.Metadata["wellName"] == "Alpha 1", "mapped facts");
    var second = found.Items.Single(item => item.ExternalId == "102");
    Expect(second.Metadata["assetName"] == "South" && second.Metadata["wellType"] == "ESP" &&
           !second.Metadata.ContainsKey("deviceType") && !second.Metadata.ContainsKey("field"), "missing facts are left out");
    var third = found.Items.Single(item => item.ExternalId == "103");
    Expect(third.DisplayName == "Well 103" && !third.Metadata.ContainsKey("wellType") && third.Metadata["wellTypeId"] == "77" &&
           !third.Metadata.ContainsKey("assetName"), "unknown type ids and unknown assets are not guessed");

    // The per-well endpoint is a fallback, not an N+1 lookup for wells already typed in the bulk result.
    var detailRoutes = new Routes(wells, assets, new Dictionary<string, string>
    {
        ["102"] = """{"id":102,"deviceType":{"id":47,"name":"ESP-VSD"},"principalDeviceId":810,"deviceName":"VSD-1"}"""
    });
    var enriched = await ForeSiteAdapter.DiscoverAsync(Client(detailRoutes), Account, CancellationToken.None);
    var enrichedWell = enriched.Items.Single(item => item.ExternalId == "102");
    Expect(enrichedWell.Metadata["deviceType"] == "ESP-VSD" &&
           enrichedWell.Metadata["deviceTypeId"] == "47" &&
           enrichedWell.Metadata["deviceId"] == "810" &&
           enrichedWell.Metadata["deviceName"] == "VSD-1",
        "device type and associated device details are recovered from Wells/Well/{id}");
    Expect(!detailRoutes.RequestedPaths.Any(path => path.EndsWith("/Wells/Well/101", StringComparison.Ordinal)) &&
           detailRoutes.RequestedPaths.Count(path => path.EndsWith("/Wells/Well/102", StringComparison.Ordinal)) == 1,
        "typed bulk wells do not cause extra device requests");

    const string nestedTypes = """
        [{"id":201,"name":"North 201","principalDeviceType":{"Id":4,"Name":"RPC-X"},
          "principalDevice":{"Id":900,"Name":"Controller-900"}}]
        """;
    var bulkRoutes = new Routes(nestedTypes, assets);
    var nested = await ForeSiteAdapter.DiscoverAsync(Client(bulkRoutes), Account, CancellationToken.None);
    var nestedWell = nested.Items.Single();
    Expect(nestedWell.Metadata["deviceType"] == "RPC-X" && nestedWell.Metadata["deviceTypeId"] == "4" &&
           nestedWell.Metadata["deviceName"] == "Controller-900" && nestedWell.Metadata["deviceId"] == "900",
        "bulk responses with nested device objects are handled without deserialization failure");
    Expect(!bulkRoutes.RequestedPaths.Any(path => path.Contains("/Wells/Well/", StringComparison.Ordinal)),
        "complete device information in the bulk response needs no per-well calls");

    var manyWells = "[" + string.Join(",", Enumerable.Range(1, 29).Select(id => $"{{\"id\":{id}}}")) + "]";
    var cappedRoutes = new Routes(manyWells, assets);
    var capped = await ForeSiteAdapter.DiscoverAsync(Client(cappedRoutes), Account, CancellationToken.None);
    Expect(capped.Complete && capped.Items.Count == 29 &&
           cappedRoutes.RequestedPaths.Count(path => path.Contains("/Wells/Well/", StringComparison.Ordinal)) == 25 &&
           capped.Warnings.Any(item => item.Contains("limited to 25 wells", StringComparison.Ordinal)),
        "missing device metadata is bounded and does not discard discovered wells");

    var noNames = await ForeSiteAdapter.DiscoverAsync(Client(new Routes(wells, null)), Account, CancellationToken.None);
    Expect(noNames.Complete && noNames.Items.Count == 3 && noNames.Warnings.Any(item => item.Contains("asset names", StringComparison.Ordinal)),
        "unreadable asset names cost a warning, not the discovery");
    Expect(noNames.Items.All(item => !item.Metadata.ContainsKey("assetName")), "no names are invented");

    foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.NotFound, HttpStatusCode.InternalServerError })
    {
        var failed = await ForeSiteAdapter.DiscoverAsync(Client(new Stub(status)), Account, CancellationToken.None);
        Expect(!failed.Complete && failed.Items.Count == 0 && failed.Warnings.Count == 1, $"{(int)status} must be an incomplete answer with a reason");
        Expect(!failed.Warnings[0].Contains(Leaky, StringComparison.Ordinal), "ForeSite's response body must never reach the person");
    }

    var refused = await ForeSiteAdapter.DiscoverAsync(Client(new Failing(new HttpRequestException("no route"))), Account, CancellationToken.None);
    Expect(!refused.Complete && refused.Items.Count == 0 && !refused.Warnings[0].Contains("no route", StringComparison.Ordinal),
        "an unreachable ForeSite is incomplete and does not leak exception text");
    var timedOut = await ForeSiteAdapter.DiscoverAsync(Client(new Failing(new TaskCanceledException())), Account, CancellationToken.None);
    Expect(!timedOut.Complete && timedOut.Warnings[0].Contains("90 seconds", StringComparison.Ordinal), "a timeout is incomplete");
    var garbage = await ForeSiteAdapter.DiscoverAsync(Client(new Routes("<html>not json</html>", assets)), Account, CancellationToken.None);
    Expect(!garbage.Complete && garbage.Items.Count == 0, "an answer that is not a list of wells is incomplete");

    var empty = await ForeSiteAdapter.DiscoverAsync(Client(new Routes("[]", assets)), Account, CancellationToken.None);
    Expect(empty.Complete && empty.Items.Count == 0 && empty.Warnings.Count == 0, "a genuinely empty ForeSite is complete");

    await TokenAndRefreshChecks();
    Expect(ForeSiteAdapter.Descriptor.CanDiscover && ForeSiteAdapter.Descriptor.GroupingFields!.Select(item => item.Key)
        .SequenceEqual(["assetName", "wellType", "deviceType", "field", "lease"]), "ForeSite declares discovery and its grouping facts");
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
    var invalid = await ForeSiteAdapter.CheckAsync(Client(new Routes("[]", "<html>invalid</html>")), Account, CancellationToken.None);
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

internal sealed class Routes(string wells, string? assets, Dictionary<string, string>? details = null) : HttpMessageHandler
{
    public List<string> RequestedPaths { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        RequestedPaths.Add(path);
        var body = path.EndsWith("/TokenService/CreateToken", StringComparison.Ordinal) ? "{}"
            : path.EndsWith("/Wells/All", StringComparison.Ordinal) ? wells
            : path.EndsWith("/SurfaceNetworkService/Asset/All", StringComparison.Ordinal) ? assets
            : path.Contains("/Wells/Well/", StringComparison.Ordinal) &&
              details is not null && details.TryGetValue(path.Split('/').Last(), out var detail) ? detail : null;
        return Task.FromResult(body is null
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
    }
}
