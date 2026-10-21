using System.Net;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;

// Recorded-shape checks always run. The live check runs when FORESITE_BASE_URL is set, as the current Windows
// identity, which is how the installed Gateway reaches ForeSite.
const string Account = @"CORP\svc-wsa";
const string Leaky = "SECRET-BODY-TEXT";

await Case(HttpStatusCode.OK, AdapterTestOutcomes.Healthy, "allowed");
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
Expect(requested.Path == "/prefix/SurfaceNetworkService/Asset/All" && requested.Method == HttpMethod.Get,
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
         {"id":103,"name":null,"assetId":1,"wellType":77},
         {"id":104,"name":"Loose 1","assetId":null,"         wellType":1},
                  {"assetId":2,"name":"No id"}]
        """;
    const string assets = """[{"id":0,"name":"Not Mapped"},{"id":1,"name":"North"},{"id":2,"name":"South"}]""";
    const string deviceTypes = """{"values":[{"wellId":102,"deviceType":"WFordKS"},{"wellId":104,"deviceType":"AEBEAM"},{"wellId":103,"deviceType":""}]}""";

    var found = await ForeSiteAdapter.DiscoverAsync(Client(new Routes(wells, assets, deviceTypes)), Account, CancellationToken.None);
    Expect(found.Complete && found.ActingAs == Account, "a good answer is complete and names the account");
    Expect(found.Items.Count == 4, "mapped and unmapped wells are found and wells without an id are skipped");
    var loose = found.Items.Single(item => item.ExternalId == "104");
    Expect(!loose.Metadata.ContainsKey("assetName") && loose.Metadata["deviceType"] == "AEBEAM", "an unmapped well is found and gets its device type");
    Expect(found.Warnings.Count == 1 && found.Warnings[0].Contains("had no id", StringComparison.Ordinal), "a skipped well is reported");
    var first = found.Items.Single(item => item.ExternalId == "101");
    Expect(first.DisplayName == "Alpha 1" && first.ProviderType == ForeSiteAdapter.WellResourceKind && first.ParentExternalId is null,
        "name, kind and no parent");
    Expect(first.Metadata["assetName"] == "North" && first.Metadata["wellType"] == "RRL" && first.Metadata["deviceType"] == "RPC" &&
           first.Metadata["field"] == "Alpha" && first.Metadata["lease"] == "L-1" && first.Metadata["facilityId"] == "7" &&
           first.Metadata["assetId"] == "1" && first.Metadata["wellTypeId"] == "1" && first.Metadata["wellId"] == "101" &&
           first.Metadata["wellName"] == "Alpha 1", "mapped facts");
    var second = found.Items.Single(item => item.ExternalId == "102");
    Expect(second.Metadata["assetName"] == "South" && second.Metadata["wellType"] == "ESP" &&
           second.Metadata["deviceType"] == "WFordKS" && !second.Metadata.ContainsKey("field"), "device type comes from the control group data");
    var third = found.Items.Single(item => item.ExternalId == "103");
    Expect(third.DisplayName == "Well 103" && !third.Metadata.ContainsKey("wellType") && third.Metadata["wellTypeId"] == "77" &&
           !third.Metadata.ContainsKey("deviceType"), "unknown type ids and blank device types are not guessed");

    var noNames = await ForeSiteAdapter.DiscoverAsync(Client(new Routes(wells, null, deviceTypes)), Account, CancellationToken.None);
    Expect(noNames.Complete && noNames.Items.Count == 4 && noNames.Warnings.Any(item => item.Contains("asset names", StringComparison.Ordinal)),
        "unreadable asset names cost a warning, not the discovery");
    Expect(noNames.Items.All(item => !item.Metadata.ContainsKey("assetName")), "no names are invented");

    var noDevices = await ForeSiteAdapter.DiscoverAsync(Client(new Routes(wells, assets, null)), Account, CancellationToken.None);
    Expect(noDevices.Complete && noDevices.Items.Count == 4 && noDevices.Warnings.Any(item => item.Contains("device types", StringComparison.Ordinal)),
        "unreadable device types cost a warning, not the discovery");
    Expect(noDevices.Items.Single(item => item.ExternalId == "101").Metadata["deviceType"] == "RPC" &&
           !noDevices.Items.Single(item => item.ExternalId == "102").Metadata.ContainsKey("deviceType"), "no device types are invented");

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
    var garbage = await ForeSiteAdapter.DiscoverAsync(Client(new Routes("<html>not json</html>", assets, deviceTypes)), Account, CancellationToken.None);
    Expect(!garbage.Complete && garbage.Items.Count == 0, "an answer that is not a list of wells is incomplete");

    var empty = await ForeSiteAdapter.DiscoverAsync(Client(new Routes("[]", assets, """{"values":[]}""")), Account, CancellationToken.None);
    Expect(empty.Complete && empty.Items.Count == 0 && empty.Warnings.Count == 0, "a genuinely empty ForeSite is complete");

    Expect(ForeSiteAdapter.Descriptor.CanDiscover && ForeSiteAdapter.Descriptor.GroupingFields!.Select(item => item.Key)
        .SequenceEqual(["assetName", "wellType", "deviceType", "field", "lease"]), "ForeSite declares discovery and its grouping facts");
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

internal sealed class Stub(HttpStatusCode status) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("SECRET-BODY-TEXT") });
}

internal sealed class Failing(Exception failure) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested ? throw new OperationCanceledException(cancellationToken) : throw failure;
}

internal sealed class Recording : HttpMessageHandler
{
    public string? Path { get; private set; }
    public HttpMethod? Method { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Path = request.RequestUri?.AbsolutePath;
        Method = request.Method;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}

internal sealed class Routes(string wells, string? assets, string? deviceTypes) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        string? body = null;
        if (path.EndsWith("/Wells/UserAssets", StringComparison.Ordinal)) body = assets;
        else if (path.EndsWith("/WellConfig/GetWellAutonomousControlGroupData", StringComparison.Ordinal)) body = deviceTypes;
        else if (path.EndsWith("/Wells/Well/Filter", StringComparison.Ordinal)) body = wells;

        return body is null
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}