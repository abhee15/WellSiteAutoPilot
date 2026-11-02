using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Domain.Control;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Http.ServiceCallers;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Server.Control;

/// <summary>
/// The Provider sink sends a command once and only believes a closed answer. Everything else must come out as a thrown
/// failure (the gate then records "ambiguous") and must never be sent a second time.
/// </summary>
internal static class GatewayControlCommandSinkChecks
{
    private const string Key = "fixture-caller-key";

    public static async Task RunAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { [ServiceCallerClient.KeyConfigurationName] = Key }).Build();
        var request = Request();

        // A closed Sent answer: exactly one authenticated call, to the command route, carrying the target and the idempotency key.
        var sent = new Recorder(HttpStatusCode.OK, """{"outcome":"Sent","code":null,"reference":"sim-ref-1"}""");
        var result = await Sink(sent, configuration).DispatchAsync(request);
        Require(result is { Accepted: true, Reference: "sim-ref-1" }, "A Sent answer was not accepted with the provider reference.");
        Require(sent.Calls.Count == 1 && sent.Calls[0].Path == "/api/internal/v1/adapters/simulator/command", "A command was not sent once to the command route.");
        Require(sent.Calls[0].Authorization == ServiceCaller.Server + ":" + Key, "The command did not carry the server's caller key.");
        using (var body = JsonDocument.Parse(sent.Calls[0].Body))
        {
            var root = body.RootElement;
            Require(root.GetProperty("requestId").GetString() == request.IdempotencyKey, "The idempotency key was not sent as the request id.");
            Require(root.GetProperty("externalId").GetString() == "WELL-101" && root.GetProperty("command").GetString() == "SET_SPEED"
                && root.GetProperty("value").GetDecimal() == 9m, "The command, target or value was not sent.");
            Require(root.GetProperty("target").GetProperty("channel").GetString() == "7", "The catalog target fields were not sent.");
        }

        // A Sent answer with no reference still leaves a stable one.
        var bare = await Sink(new Recorder(HttpStatusCode.OK, """{"outcome":"Sent"}"""), configuration).DispatchAsync(request);
        Require(bare.Accepted && bare.Reference == $"gateway:{request.ActionId:N}", "A Sent answer without a reference did not get a stable one.");

        // Rejected is a known "did not act": only a short code survives, provider text never does.
        var rejected = await Sink(new Recorder(HttpStatusCode.OK, """{"outcome":"Rejected","code":"SIM_VALUE_OUT_OF_RANGE"}"""), configuration).DispatchAsync(request);
        Require(rejected is { Accepted: false, Reference: "SIM_VALUE_OUT_OF_RANGE" }, "A Rejected answer did not keep its code.");
        var leaky = await Sink(new Recorder(HttpStatusCode.OK, """{"outcome":"Rejected","code":"private provider text"}"""), configuration).DispatchAsync(request);
        Require(leaky is { Accepted: false, Reference: "PROVIDER_REJECTED" }, "Provider text leaked through a rejection code.");

        // Anything else is ambiguous: it throws, carries no private text, and is sent exactly once.
        foreach (var (status, text) in new[]
        {
            (HttpStatusCode.OK, """{"outcome":"DryRun"}"""),
            (HttpStatusCode.OK, """{"outcome":"Surprise"}"""),
            (HttpStatusCode.OK, "<html>private login</html>"),
            (HttpStatusCode.OK, "null"),
            (HttpStatusCode.BadGateway, "private upstream text"),
            (HttpStatusCode.InternalServerError, "private upstream text"),
            (HttpStatusCode.NotFound, ""),
            (HttpStatusCode.Forbidden, "")
        })
        {
            var recorder = new Recorder(status, text);
            await ExpectAmbiguousAsync(() => Sink(recorder, configuration).DispatchAsync(request), $"{status} {text}");
            Require(recorder.Calls.Count == 1, $"A {status} answer caused {recorder.Calls.Count} sends.");
        }
        foreach (var timeout in new[] { false, true })
        {
            var recorder = new Recorder(timeout ? new TaskCanceledException("private timeout text") : new HttpRequestException("private transport text"));
            await ExpectAmbiguousAsync(() => Sink(recorder, configuration).DispatchAsync(request), "transport " + timeout);
            Require(recorder.Calls.Count == 1, "A transport failure was retried.");
        }

        // No key, no send.
        var keyless = new Recorder(HttpStatusCode.OK, """{"outcome":"Sent"}""");
        await ExpectAmbiguousAsync(() => Sink(keyless, new ConfigurationBuilder().Build()).DispatchAsync(request), "no key");
        Require(keyless.Calls.Count == 0, "A command was sent without the server's key.");

        // Caller cancellation is cancellation, not an ambiguous Gateway failure.
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancellationKept = false;
        try { await Sink(new Recorder(new TaskCanceledException()), configuration).DispatchAsync(request, cancelled.Token); }
        catch (OperationCanceledException) { cancellationKept = true; }
        Require(cancellationKept, "Caller cancellation was converted into a Gateway failure.");

        // Readback asks for the adapter's own readback quantity, never the commanded one.
        var read = new Recorder(HttpStatusCode.OK, Value(9m, "spm", "Good"));
        var applied = await Sink(read, configuration).ReadbackAsync(request);
        Require(applied is { State: ControlReadbackState.Applied, ObservedValue: 9m }, "A good readback was not reported as Applied.");
        Require(read.Calls.Count == 1 && read.Calls[0].Path == "/api/internal/v1/adapters/simulator/command-readback", "Readback did not use the readback route.");
        using (var body = JsonDocument.Parse(read.Calls[0].Body))
            Require(body.RootElement.GetProperty("quantity").GetString() == "CurrentSPM", "Readback did not ask for the adapter's readback quantity.");

        // Not a good reading in the commanded unit means "cannot say", never a guess.
        foreach (var (text, why) in new[]
        {
            (Value(9m, "spm", "Bad"), "bad quality"),
            (Value(9m, "spm", "Uncertain"), "uncertain quality"),
            (Value(9m, "rpm", "Good"), "different unit"),
            ("""{"quantity":"CurrentSPM","value":null,"unit":"spm","timestampUtc":"2026-11-02T10:00:00Z","quality":"Good","source":"sim"}""", "no value")
        })
        {
            var unknown = await Sink(new Recorder(HttpStatusCode.OK, text), configuration).ReadbackAsync(request);
            Require(unknown.State == ControlReadbackState.Unknown && unknown.ObservedValue is null, $"A readback with {why} was not Unknown.");
        }

        // A command the adapter cannot read back, or one it does not define, is never read at all.
        foreach (var command in new[] { "PUMP_ACK", "UNKNOWN_COMMAND" })
        {
            var quiet = new Recorder(HttpStatusCode.OK, Value(9m, "spm", "Good"));
            var none = await Sink(quiet, configuration).ReadbackAsync(request with { Command = command });
            Require(none.State == ControlReadbackState.Unknown && quiet.Calls.Count == 0, $"{command} was read back.");
        }
        var noTarget = await Sink(new Recorder(HttpStatusCode.OK, ""), configuration).ReadbackAsync(request with { Target = null });
        Require(noTarget.State == ControlReadbackState.Unknown, "A readback without a target did not report Unknown.");

        // A dispatch without a target or value is a programming error, never a send.
        var unsent = new Recorder(HttpStatusCode.OK, """{"outcome":"Sent"}""");
        foreach (var broken in new[] { request with { Target = null }, request with { Value = null } })
        {
            try { await Sink(unsent, configuration).DispatchAsync(broken); }
            catch (InvalidOperationException) { continue; }
            throw new InvalidOperationException("An incomplete command was sent.");
        }
        Require(unsent.Calls.Count == 0, "An incomplete command reached the Gateway.");

        Console.WriteLine("The Gateway control sink sends once, trusts only closed answers, and reads back only what it can vouch for.");
    }

    private static GatewayControlCommandSink Sink(Recorder recorder, IConfiguration configuration) =>
        new(new HttpClient(recorder) { BaseAddress = new Uri("http://gateway/") }, configuration, new Adapters());

    private static ControlDispatchRequest Request() => new(Guid.NewGuid(), Guid.NewGuid(), "SET_SPEED", "SPM", 9m, "spm",
        "control-action:" + Guid.NewGuid().ToString("N"),
        new ControlTarget(Guid.NewGuid(), 4, "simulator", "WELL-101", new Dictionary<string, string?> { ["baseUrl"] = "http://private-fixture" },
            new ConnectionCommandCatalogEntry("SET_SPEED", true, 0, 12, null, new Dictionary<string, string> { ["channel"] = "7" }, 2, 30), 3));

    private static string Value(decimal value, string unit, string quality) =>
        $$"""{"quantity":"CurrentSPM","value":{{value}},"unit":"{{unit}}","timestampUtc":"2026-11-02T10:00:00Z","quality":"{{quality}}","source":"sim"}""";

    private static async Task ExpectAmbiguousAsync(Func<Task> send, string label)
    {
        try { await send(); }
        catch (WellSiteAutoPilotException error) when (!error.Message.Contains("private", StringComparison.Ordinal)
            && !error.Message.Contains(Key, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException($"{label} was not reported as a safe, ambiguous failure.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Call(string Path, string? Authorization, string Body);

    private sealed class Recorder : HttpMessageHandler
    {
        private readonly HttpStatusCode status;
        private readonly string text;
        private readonly Exception? failure;

        public Recorder(HttpStatusCode status, string text) { this.status = status; this.text = text; }

        public Recorder(Exception failure) { this.failure = failure; text = ""; }

        public List<Call> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(new Call(request.RequestUri!.AbsolutePath, request.Headers.Authorization?.Parameter,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (failure is not null) throw failure;
            return new HttpResponseMessage(status) { Content = new StringContent(text, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Adapters : IGatewayAdapters
    {
        public Task<IReadOnlyList<AdapterDescriptor>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdapterDescriptor>>([new AdapterDescriptor("simulator", "Simulator", "", 1, [], [],
                Commands:
                [
                    Definition("SET_SPEED", AdapterCommandVerification.ReadBack, "CurrentSPM"),
                    Definition("PUMP_ACK", AdapterCommandVerification.AcknowledgementOnly, null)
                ])]);

        public Task<IReadOnlyList<AdapterSettingProblem>?> ValidateAsync(string adapterKey, IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdapterTestResult?> TestAsync(string adapterKey, IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DiscoveryResult?> DiscoverAsync(string adapterKey, IReadOnlyDictionary<string, string?> settings, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static AdapterCommandDefinition Definition(string name, AdapterCommandVerification verification, string? quantity) =>
            new(name, name, "", "SPM", "spm", AdapterCommandValueKind.Number, [], verification, quantity, 2, 60, 30, 300);
    }
}
