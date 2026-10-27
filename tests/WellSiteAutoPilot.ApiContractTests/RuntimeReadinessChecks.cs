using System.Net;
using System.Text;
using WellSiteAutoPilot.Domain.System;
using WellSiteAutoPilot.Infrastructure.System;

internal static class RuntimeReadinessChecks
{
    public static async Task RunAsync()
    {
        using var client = new HttpClient(new HealthResponses());
        var service = new PlatformInformationService(client, "http://gateway", "http://worker");
        var components = await service.GetComponentsAsync();
        var worker = components.Single(c => c.Name == PlatformComponentNames.DotNetRuntime);
        var gateway = components.Single(c => c.Name == PlatformComponentNames.Gateway);
        if (worker.Status != PlatformComponentStatus.Healthy || worker.ModuleSdkVersion != "1.1.0" ||
            gateway.Status != PlatformComponentStatus.Healthy || gateway.ModuleSdkVersion is not null)
            throw new InvalidOperationException("Readiness lost the worker SDK or rejected legacy plain-text health.");
        Console.WriteLine("Worker SDK and legacy health response checks passed.");
    }

    private sealed class HealthResponses : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                request.RequestUri!.Host == "worker" ? """{"status":"Healthy","runtime":"DotNet","moduleSdkVersion":"1.1.0"}""" : "Healthy",
                Encoding.UTF8, "application/json") });
    }
}
