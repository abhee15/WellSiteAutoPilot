using System.Net;
using System.Text;
using System.Text.Json;
using WellSiteAutoPilot.Integration.ForeSite;

internal static class ForeSiteDiscoveryChecks
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string Account = @"CORP\svc-wsa";
    private const string Assets = """[{"Id":2,"Name":"South"},{"Id":"1","Name":"North"},{"Id":0,"Name":"Not Mapped"},{"Id":1,"Name":"North"}]""";
    private const string Wells = """
        [{"Id":101,"Name":"Alpha 1","AssetId":1,"WellType":1,"Field":"Alpha","Lease":"L-1","FacilityId":"RPOC_101","PrincipalDeviceType":"RPC"},
         {"Id":"102","Name":"Alpha 2","AssetId":"2","WellType":"2"},
         {"Id":103,"AssetId":1,"WellType":77},
         {"Id":104,"Name":"Loose 1","AssetId":null},
         {"Id":105,"Name":"Loose 2","AssetId":0},
         {"Id":104,"Name":"Loose 1","AssetId":null}]
        """;
    private const string Devices = """{"Units":null,"Values":[{"WellId":102,"DeviceType":"WFordKS"},{"WellId":"104","DeviceType":"AEBEAM"},{"WellId":103,"DeviceType":" "},{"WellId":999,"DeviceType":"FOREIGN"}]}""";

    public static async Task VerifyAsync()
    {
        var routes = new DiscoveryRoutes(Assets, Wells, Devices);
        var result = await Discover(routes);
        Expect(result.Complete && result.ActingAs == Account && result.Items.Count == 5, "complete, unique mapped and unmapped wells");
        Expect(result.Warnings.Count == 0 && result.UnavailableFields is { Count: 0 }, "successful enrichment declares available grouping facts");
        Expect(result.SourceScope is not null && result.SourceScope.SequenceEqual(["0", "1", "2"]), "authorized asset scope, including unmapped, is retained as evidence");
        var missingIdentity = await Discover(new DiscoveryRoutes(Assets, "[{\"Name\":\"No id\",\"AssetId\":1}]", Devices));
        Expect(!missingIdentity.Complete && missingIdentity.Warnings.Any(warning => warning.Contains("had no id", StringComparison.Ordinal)),
            "a missing well identity makes membership incomplete");
        Expect(routes.Requests.Select(request => request.Path).SequenceEqual([
            ForeSiteAdapter.TokenPath, ForeSiteAdapter.UserAssetsPath, ForeSiteAdapter.WellsFilterPath, ForeSiteAdapter.DeviceTypesPath]), "user assets precede both filtered requests; no unfiltered endpoint");
        Expect(routes.Requests[1].Method == HttpMethod.Get, "user assets use GET");
        foreach (var request in routes.Requests.Skip(2))
        {
            Expect(request.Method == HttpMethod.Post && Scope(request.Body).SequenceEqual([0L, 1L, 2L]), "both POSTs use the identical nonempty deduplicated scope");
        }
        Expect(routes.Requests.Skip(1).All(request => request.Cookie == "session=1"), "same session cookie retained across GET and POST");
        var first = result.Items.Single(item => item.ExternalId == "101");
        Expect(first.DisplayName == "Alpha 1" && first.ProviderType == ForeSiteAdapter.WellResourceKind && first.ParentExternalId is null, "well identity unchanged");
        Expect(first.Metadata["assetName"] == "North" && first.Metadata["wellType"] == "RRL" && first.Metadata["deviceType"] == "RPC" &&
            first.Metadata["facilityId"] == "RPOC_101" && first.Metadata["field"] == "Alpha" && first.Metadata["lease"] == "L-1", "well facts and device fallback preserved");
        var second = result.Items.Single(item => item.ExternalId == "102");
        Expect(second.Metadata["assetName"] == "South" && second.Metadata["wellType"] == "ESP" && second.Metadata["deviceType"] == "WFordKS", "device data joins by well ID; string numeric IDs supported");
        var unknown = result.Items.Single(item => item.ExternalId == "103");
        Expect(unknown.DisplayName == "Well 103" && unknown.Metadata["wellTypeId"] == "77" && !unknown.Metadata.ContainsKey("wellType") && !unknown.Metadata.ContainsKey("deviceType"), "unknown types remain unknown");
        Expect(result.Items.Where(item => item.ExternalId is "104" or "105").All(item => item.Metadata["assetName"] == "Not Mapped" && item.Metadata["assetId"] == "0"), "null and zero assets share one stable unmapped identity");

        var noMapped = new DiscoveryRoutes("[]", """[{"Id":104,"AssetId":null}]""", """{"Values":[]}""");
        Expect((await Discover(noMapped)).Complete && noMapped.Requests.Skip(2).All(request => Scope(request.Body).SequenceEqual([0L])), "no mapped assets still sends [0], never an empty broad filter");
        var noUnmapped = await Discover(new DiscoveryRoutes("""[{"Id":1,"Name":"North"}]""", "[]", Devices));
        Expect(noUnmapped.Complete && noUnmapped.Items.Count == 0, "ForeSite may deny unmapped wells; do not invent them");
        foreach (var invalid in new[] { "null", "{}", "<html>bad</html>", "[null]", "[{\"Id\":null}]", "[{\"Id\":-1}]" })
        {
            var malformed = new DiscoveryRoutes(invalid, Wells, Devices);
            var failed = await Discover(malformed);
            Expect(!failed.Complete && failed.Items.Count == 0 && malformed.Requests.Count == 2, "invalid asset inventory must stop before any well request");
        }
        foreach (var invalid in new[] { "null", "{}", "[null]", "<html>bad</html>", "[{\"Id\":1,\"AssetId\":{}}]", "[{\"Id\":1,\"AssetId\":99}]" })
        {
            var failed = await Discover(new DiscoveryRoutes(Assets, invalid, Devices));
            Expect(!failed.Complete && failed.Items.Count == 0, "invalid or foreign well inventory is never accepted");
        }
        foreach (var invalid in new[] { "null", "{}", "{\"Values\":null}", "{\"Values\":[null]}", "<html>bad</html>" })
        {
            var partial = await Discover(new DiscoveryRoutes(Assets, Wells, invalid));
            Expect(partial.Complete && partial.Items.Count == 5 && partial.Warnings.Any(warning => warning.Contains("device types", StringComparison.Ordinal)) &&
                partial.Items.Single(item => item.ExternalId == "101").Metadata["deviceType"] == "RPC", "optional malformed devices warn and retain well-list fallback");
        }
        var deviceUnavailable = await Discover(new DiscoveryRoutes(Assets, Wells, "null"));
        Expect(deviceUnavailable.Complete && deviceUnavailable.UnavailableFields!.SequenceEqual(["deviceType"]),
            "optional device failure is field unavailability, not confirmed blank device data");
        var empty = new DiscoveryRoutes(Assets, "[]", Devices);
        Expect((await Discover(empty)).Complete && empty.Requests.Count == 3, "empty well inventory avoids costly device lookup");

        foreach (var path in new[] { ForeSiteAdapter.UserAssetsPath, ForeSiteAdapter.WellsFilterPath, ForeSiteAdapter.DeviceTypesPath })
        {
            var refresh = new DiscoveryRoutes(Assets, Wells, Devices) { FailPath = path, FailureStatus = HttpStatusCode.Unauthorized, FailureCount = 1 };
            Expect((await Discover(refresh)).Complete && refresh.TokenCount == 2, "one 401 refreshes the session at each discovery stage");
            var attempts = refresh.Requests.Where(request => request.Path == path).ToArray();
            Expect(attempts.Length == 2 && attempts[0].Body == attempts[1].Body && attempts[0].Cookie == "session=1" && attempts[1].Cookie == "session=2", "retry replays exact filter with a fresh cookie");
            foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError })
            {
                var failedRoute = new DiscoveryRoutes(Assets, Wells, Devices) { FailPath = path, FailureStatus = status, FailureCount = int.MaxValue };
                var failed = await Discover(failedRoute);
                Expect(failed.Complete == (path == ForeSiteAdapter.DeviceTypesPath) && failed.Warnings.Any(warning => warning.Contains("Known ForeSite problem", StringComparison.Ordinal)), "provider FullErrorMessage preserved; device failure optional");
                Expect(failedRoute.Requests.Count(request => request.Path == path) == (status == HttpStatusCode.Unauthorized ? 2 : 1), "only 401 retries, at most once per request");
            }
            foreach (var exception in new Exception[] { new HttpRequestException("PRIVATE-TRANSPORT-DETAIL"), new TaskCanceledException() })
            {
                var interrupted = await Discover(new DiscoveryRoutes(Assets, Wells, Devices) { FailPath = path, FailureException = exception });
                Expect(interrupted.Complete == (path == ForeSiteAdapter.DeviceTypesPath) && interrupted.Warnings.Count > 0 &&
                    interrupted.Warnings.All(warning => !warning.Contains("PRIVATE-TRANSPORT-DETAIL", StringComparison.Ordinal)), "transport/timeout errors retain stage completeness without raw exception text");
            }
            var cancelled = new DiscoveryRoutes(Assets, Wells, Devices) { CancelPath = path };
            using var cancellation = new CancellationTokenSource();
            cancelled.Cancel = cancellation.Cancel;
            try
            {
                await Discover(cancelled, cancellation.Token);
                throw new InvalidOperationException("Caller cancellation must propagate.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        var large = JsonSerializer.Serialize(Enumerable.Range(1, 10_000).Select(id => new { Id = id, AssetId = id <= 5000 ? 1 : 0 }));
        var many = await Discover(new DiscoveryRoutes(Assets, large, """{"Values":[{"WellId":10000,"DeviceType":"LastDevice"}]}"""));
        Expect(many.Complete && many.Items.Count == 10_000 && many.Items.Single(item => item.ExternalId == "10000").Metadata["deviceType"] == "LastDevice", "bulk discovery and enrichment retain all 10,000 wells");
        Console.WriteLine("ForeSite scoped discovery, POST session retry, error/cancellation and 10,000-well checks passed.");
    }

    private static async Task<WellSiteAutoPilot.Integration.Contracts.Providers.DiscoveryResult> Discover(DiscoveryRoutes routes, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(routes) { BaseAddress = new Uri("http://foresite.test/prefix/") };
        return await ForeSiteAdapter.DiscoverAsync(client, Account, cancellationToken);
    }
    private static long[] Scope(string? body) => JsonSerializer.Deserialize<Filter>(body!, JsonOptions)!.AssetIds;
    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ForeSite discovery check failed: " + message);
    }
    private sealed record Filter(long[] AssetIds);
    private sealed record Request(string Path, HttpMethod Method, string? Body, string Cookie);
    private sealed class DiscoveryRoutes(string assets, string wells, string devices) : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];
        public int TokenCount { get; private set; }
        public string? FailPath { get; init; }
        public string? CancelPath { get; init; }
        public Action? Cancel { get; set; }
        public HttpStatusCode FailureStatus { get; init; }
        public Exception? FailureException { get; init; }
        public int FailureCount { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath["/prefix/".Length..];
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var cookie = request.Headers.TryGetValues("Cookie", out var values) ? string.Join(";", values) : "";
            if (body is not null)
            {
                using var filter = JsonDocument.Parse(body);
                Expect(filter.RootElement.TryGetProperty("assetIds", out _) && !filter.RootElement.TryGetProperty("AssetIds", out _),
                    "ForeSite filter field casing changed.");
            }
            Requests.Add(new Request(path, request.Method, body, cookie));
            if (path == CancelPath) { Cancel!(); cancellationToken.ThrowIfCancellationRequested(); }
            if (path == ForeSiteAdapter.TokenPath)
            {
                var token = Json("{}");
                token.Headers.TryAddWithoutValidation("Set-Cookie", $"session={++TokenCount}; Path=/prefix");
                return token;
            }
            Expect(cookie == $"session={TokenCount}", "authenticated cookie required for every inventory request");
            if (path == FailPath && FailureException is { } exception) throw exception;
            if (path == FailPath && FailureCount-- > 0)
                return new HttpResponseMessage(FailureStatus) { Content = new StringContent("""{"FullErrorMessage":"Known ForeSite problem"}""", Encoding.UTF8, "application/json") };
            return Json(path switch
            {
                ForeSiteAdapter.UserAssetsPath => assets,
                ForeSiteAdapter.WellsFilterPath => wells,
                ForeSiteAdapter.DeviceTypesPath => devices,
                _ => throw new InvalidOperationException("Unexpected/unfiltered discovery endpoint: " + path)
            });
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
