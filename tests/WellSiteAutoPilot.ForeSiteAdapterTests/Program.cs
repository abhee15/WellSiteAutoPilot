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
