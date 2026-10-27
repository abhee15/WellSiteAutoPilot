using System.Net;
using Microsoft.Extensions.Configuration;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Http.ServiceCallers;
using WellSiteAutoPilot.Server.Connections;

internal static class GatewayMeasurementFailureChecks
{
    public static async Task RunAsync()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { [ServiceCallerClient.KeyConfigurationName] = "fixture-caller-key" }).Build();
        var read = new AuthorizedExecutionRead("foresite", Guid.NewGuid(), 1, "42", "PumpFillage",
            new Dictionary<string, string?> { ["baseUrl"] = "http://private-fixture" });
        foreach (var (status, body, expected) in new[]
        {
            (HttpStatusCode.BadGateway, "<html>private upstream login</html>", "EXECUTION_READ_GATEWAY_FAILED"),
            (HttpStatusCode.Forbidden, "", "EXECUTION_READ_GATEWAY_FAILED"),
            (HttpStatusCode.BadRequest, "{\"code\":\"FORESITE_MEASUREMENT_MISSING\",\"detail\":\"private upstream text\"}", "FORESITE_MEASUREMENT_MISSING"),
            (HttpStatusCode.BadRequest, "{\"code\":\"private upstream text\"}", "EXECUTION_READ_GATEWAY_FAILED"),
            (HttpStatusCode.OK, "<html>private login</html>", "EXECUTION_READ_GATEWAY_INVALID"),
            (HttpStatusCode.OK, "null", "EXECUTION_READ_GATEWAY_INVALID")
        })
        {
            using var http = new HttpClient(new ResponseHandler(status, body)) { BaseAddress = new Uri("http://gateway/") };
            await ExpectAsync(new GatewayAdapterClient(http, configuration), read, expected);
        }
        foreach (var timeout in new[] { false, true })
        {
            using var http = new HttpClient(new TransportFailure(timeout)) { BaseAddress = new Uri("http://gateway/") };
            await ExpectAsync(new GatewayAdapterClient(http, configuration), read,
                timeout ? FailureCodes.DependencyTimeout : FailureCodes.DependencyUnavailable);
        }
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var cancelledHttp = new HttpClient(new TransportFailure(true)) { BaseAddress = new Uri("http://gateway/") };
        try { await new GatewayAdapterClient(cancelledHttp, configuration).GetCurrentAsync(read, cancelled.Token); }
        catch (OperationCanceledException) { Console.WriteLine("Gateway measurement failures preserve safe dependency codes and caller cancellation."); return; }
        throw new InvalidOperationException("Caller cancellation was converted into a Gateway failure.");
    }

    private static async Task ExpectAsync(GatewayAdapterClient client, AuthorizedExecutionRead read, string expected)
    {
        try { await client.GetCurrentAsync(read); }
        catch (WellSiteAutoPilotException error) when (error.Code == expected &&
            !error.Message.Contains("private", StringComparison.Ordinal) &&
            !error.Message.Contains("fixture-caller-key", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Gateway measurement failure was not safely classified as " + expected);
    }

    private sealed class ResponseHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath != "/api/internal/v1/adapters/foresite/current" ||
                request.Headers.Authorization?.Parameter != ServiceCaller.Server + ":fixture-caller-key")
                throw new InvalidOperationException("Measurement acquisition lost the authenticated Gateway endpoint.");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
    private sealed class TransportFailure(bool timeout) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return timeout ? throw new TaskCanceledException("private timeout text") : throw new HttpRequestException("private transport text");
        }
    }
}
