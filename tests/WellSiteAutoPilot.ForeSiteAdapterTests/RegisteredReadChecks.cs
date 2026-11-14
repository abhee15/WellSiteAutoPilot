using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WellSiteAutoPilot.Http;
using WellSiteAutoPilot.Http.ServiceCallers;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.IntegrationGateway.Adapters;

internal static class RegisteredReadChecks
{
    public static async Task RunAsync()
    {
        const string adapterKey = "test-laboratory";
        const string resourceKind = "test-laboratory.sample";
        const string operationKey = "lab.sample.certificate";
        const string permissionKey = "registered-read-test-server";
        const string workerKey = "registered-read-test-worker";
        var operation = new AdapterOperationDescriptor(operationKey, "Certificate", "Read a sample certificate.",
            ProviderCapabilityKind.ExtensionRead, "test-laboratory.certificate", "1.0",
            new("lab.certificate-request/v1", [new("language", "string", true, "Certificate language.")]),
            new("lab.certificate/v1", [new("certificateId", "string", true, "Certificate identifier.")]),
            "Document source", "No quality field", new(1, 128, 256, TimeoutSeconds: 2), ResourceKind: resourceKind);
        var descriptor = new AdapterDescriptor(adapterKey, "Laboratory", "Test adapter.", 1, [],
            [new(ProviderCapabilityKind.ExtensionRead, "Lab certificate")], Operations: [operation],
            OperationGroups: [new("lab-documents", "Lab documents", "Documents.", "1.0", [operationKey])]);
        var invocations = 0;
        var handler = AdapterReadRegistration.Create<CertificateRequest, Certificate>(operation, (input, invocation, _, _) =>
        {
            Interlocked.Increment(ref invocations);
            return Task.FromResult(new Certificate(input.Language == "oversized" ? new string('x', 300) : invocation.ExternalId));
        });
        RequireInvalidCatalog(new(descriptor, []));
        RequireInvalidCatalog(new(descriptor, [handler, handler]));
        var catalog = new AdapterCatalog(additionalAdapters: [new(descriptor, [handler])]);
        Require(catalog.Find(adapterKey)?.ReadCapabilityGroups().Single().OperationKeys.Single() == operationKey &&
            catalog.Find("petrovisor")?.ReadOperations().All(item => item.Key != operationKey) == true,
            "only the registering adapter advertises the new operation");

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["Security:Callers:server:Verifier"] = ServiceCallerKeys.VerifierFor(permissionKey);
        builder.Configuration["Security:Callers:worker:Verifier"] = ServiceCallerKeys.VerifierFor(workerKey);
        builder.Services.AddServiceCallerAuthentication(builder.Configuration);
        builder.Services.AddWellSiteHttpErrorHandling();
        builder.Services.AddSingleton(catalog);
        await using var app = builder.Build();
        app.UseWellSiteHttpErrorHandling();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("/api/internal/v1").MapRegisteredAdapterReads();
        await app.StartAsync();

        using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var request = new AdapterReadInvocation<CertificateRequest>(Guid.NewGuid(), 1, new Dictionary<string, string?>(),
            resourceKind, "sample-42", "1.0", new("en"));
        await Expect(adapterKey, request, HttpStatusCode.Unauthorized, caller: null);
        await Expect(adapterKey, request, HttpStatusCode.Forbidden, caller: "worker:" + workerKey);
        await Expect("petrovisor", request, HttpStatusCode.NotFound);
        await Expect(adapterKey, request with { ResourceKind = "foresite.well" }, HttpStatusCode.BadRequest);
        await Expect(adapterKey, request with { ContractVersion = "2.0" }, HttpStatusCode.Conflict);
        await Expect(adapterKey, request with { ConnectionId = Guid.Empty }, HttpStatusCode.BadRequest);
        await Expect(adapterKey, request with { Input = new(new string('x', MeasurementPayloadLimits.MaximumReplyBytes)) },
            HttpStatusCode.RequestEntityTooLarge);
        Require(invocations == 0, "rejected requests never invoke the adapter");
        await Expect(adapterKey, request, HttpStatusCode.OK);
        await Expect(adapterKey, request with { Input = new("oversized") }, HttpStatusCode.ServiceUnavailable);
        Require(invocations == 2, "only requests matching the registration reach the handler");
        Console.WriteLine("Registered adapter reads: custom resource and schema, catalog parity, authorization, version and payload bounds passed.");

        async Task Expect(string adapter, AdapterReadInvocation<CertificateRequest> body, HttpStatusCode status,
            string? caller = "server:" + permissionKey)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/v1/adapters/{adapter}/reads/{operationKey}")
            {
                Content = JsonContent.Create(body)
            };
            if (caller is not null) message.Headers.Authorization = new AuthenticationHeaderValue(ServiceCaller.Scheme, caller);
            using var response = await http.SendAsync(message);
            Require(response.StatusCode == status, $"expected {(int)status}, got {(int)response.StatusCode}");
            if (status == HttpStatusCode.OK)
                Require((await response.Content.ReadFromJsonAsync<Certificate>())?.CertificateId == "sample-42",
                    "registered handler returns its typed result");
        }
    }

    private static void RequireInvalidCatalog(AdapterRegistration registration)
    {
        try { _ = new AdapterCatalog(additionalAdapters: [registration]); }
        catch (InvalidOperationException) { return; }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Invalid adapter operation registration passed startup validation.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Registered adapter read: " + message);
    }

    private sealed record CertificateRequest([property: System.Text.Json.Serialization.JsonRequired] string Language);
    private sealed record Certificate(string CertificateId);
}
