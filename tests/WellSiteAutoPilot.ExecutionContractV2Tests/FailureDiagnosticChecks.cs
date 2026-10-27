using System.Net;
using System.Text;
using System.Text.Json;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Worker.DotNet.Execution;

internal static class FailureDiagnosticChecks
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static async Task RunAsync(ExecutionRequestedV2 request, DateTimeOffset now)
    {
        using var http = new HttpClient(new BadMeasurement(now.AddMinutes(-10))) { BaseAddress = new Uri("http://fixture") };
        var engine = new ModuleExecutionEngine(new GatewayDataClient(http), new BuiltInLogicModuleResolver());
        var many = request with { ModuleId = "weatherford.sample-shadow-read", Inputs = Enumerable.Range(0, 100).Select(i => request.Inputs.First() with
            { RequirementId = "input-" + i, MaximumAgeSeconds = 60 }).ToArray() };
        try
        {
            await engine.ExecuteObservedAsync(many, "diagnostic-bounds", now);
            throw new InvalidOperationException("Stale inputs reached the module.");
        }
        catch (WellSiteAutoPilotException error) when (error.Code == "EXECUTION_INPUT_STALE")
        {
            var observations = error.Data.Values.OfType<ExecutionInputDiagnosticV1[]>().Single();
            var json = JsonSerializer.Serialize(observations, JsonOptions);
            if (observations.Length != 64 || observations.Any(item => item.Value != 80 || item.OutcomeCode != "EXECUTION_INPUT_STALE") ||
                json.Contains("SECRET", StringComparison.Ordinal) || json.Contains("providerMapping", StringComparison.Ordinal))
                throw new InvalidOperationException("Failed-input diagnostics lost observations, bounds or secret exclusion.");
        }
        Console.WriteLine("Failed-input evidence preserves stale values and reasons, caps observations at 64 and excludes raw provider metadata.");
    }
    private sealed class BadMeasurement(DateTimeOffset time) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            { quantity = "PumpFillage", value = 80, unit = "%", timestampUtc = time, quality = "Good", source = "SECRET-provider-response" }, JsonOptions),
                Encoding.UTF8, "application/json") });
    }
}
