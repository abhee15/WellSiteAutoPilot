using System.Net;
using System.Text;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.IntegrationGateway.Adapters;
using WellSiteAutoPilot.IntegrationGateway.Providers;

/// <summary>
/// The Gateway's simulator command client reports only a definite answer and sends exactly once. Anything it cannot be
/// sure of is thrown, never reported as Sent, because the caller treats that as ambiguous and must not retry.
/// </summary>
internal static class SimulatorCommandProviderChecks
{
    public static async Task RunAsync()
    {
        var simulator = new AdapterCatalog().Find("simulator") ?? throw new InvalidOperationException("The simulator adapter is missing.");
        Require(simulator.Commands?.Select(item => item.Name).Order(StringComparer.Ordinal).SequenceEqual(["PUMP_RUN", "SET_SPEED"]) == true,
            "the simulator must declare its two commands");
        Require(simulator.Commands!.All(item => item.Verification == AdapterCommandVerification.ReadBack && item.TargetFields.Count == 0),
            "simulator commands are read-back verified and need no target fields");
        Require(new AdapterCatalog().List().Where(item => item.Commands is { Count: > 0 }).Select(item => item.Key).SequenceEqual(["simulator"]),
            "only the simulator carries commands until an adapter owner supplies a behaviour sheet");

        var sent = await Send(HttpStatusCode.OK, """{"status":"Accepted","reference":"sim-1"}""");
        Require(sent.Response is { Outcome: GatewayCommandOutcome.Sent, Reference: "sim-1" } && sent.Calls == 1, "200 is Sent, once");
        var unreadable = await Send(HttpStatusCode.OK, "not json");
        Require(unreadable.Response?.Outcome == GatewayCommandOutcome.Sent, "an unreadable success body is still Sent");

        var rejected = await Send(HttpStatusCode.UnprocessableEntity, """{"status":"Rejected","code":"VALUE_OUT_OF_RANGE"}""");
        Require(rejected.Response is { Outcome: GatewayCommandOutcome.Rejected, Code: "VALUE_OUT_OF_RANGE" } && rejected.Calls == 1,
            "422 is a definite Rejected with the provider's code");
        var notFound = await Send(HttpStatusCode.NotFound, "");
        Require(notFound.Response is { Outcome: GatewayCommandOutcome.Rejected, Code: "REJECTED_BY_PROVIDER" }, "404 without a code is Rejected");

        foreach (var status in new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway, HttpStatusCode.Unauthorized })
        {
            var unknown = await Send(status, """{"code":"X"}""");
            Require(unknown.Failure is { Kind: FailureKind.DependencyUnavailable } && unknown.Response is null && unknown.Calls == 1,
                $"{(int)status} says nothing about whether the provider acted: unknown, sent once, never retried");
        }

        var unreachable = await Send(new ThrowingHandler(new HttpRequestException("no route")));
        Require(unreachable.Failure?.Kind == FailureKind.DependencyUnavailable && unreachable.Calls == 1, "a transport failure is unknown");
        var timedOut = await Send(new ThrowingHandler(new TaskCanceledException("timeout")));
        Require(timedOut.Failure?.Kind == FailureKind.DependencyTimeout && timedOut.Calls == 1, "a timeout is unknown, not a rejection");

        var bad = await Send(HttpStatusCode.OK, "{}", requestId: " ");
        Require(bad.Failure?.Kind == FailureKind.Validation && bad.Calls == 0, "a command without a request id is never sent");
    }

    private static Task<Result> Send(HttpStatusCode status, string body, string requestId = "r1") =>
        Send(new CountingHandler(status, body), requestId);

    private static async Task<Result> Send(HttpMessageHandler handler, string requestId = "r1")
    {
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5091") };
        var provider = new SimulatorCommandProvider(client);
        var request = new GatewayCommandRequest(Guid.NewGuid(), 1, [], "WELL-101", "SET_SPEED", requestId, 7m, "spm", []);
        try
        {
            var response = await provider.SendAsync(request, CancellationToken.None);
            return new(response, null, Calls(handler));
        }
        catch (WellSiteAutoPilotException failure)
        {
            return new(null, failure, Calls(handler));
        }
    }

    private static int Calls(HttpMessageHandler handler) => handler switch
    {
        CountingHandler counting => counting.Calls,
        ThrowingHandler throwing => throwing.Calls,
        _ => throw new InvalidOperationException()
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"Simulator command provider check failed: {message}");
    }

    private sealed record Result(GatewayCommandResponse? Response, WellSiteAutoPilotException? Failure, int Calls);

    private sealed class CountingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class ThrowingHandler(Exception error) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw error;
        }
    }
}
