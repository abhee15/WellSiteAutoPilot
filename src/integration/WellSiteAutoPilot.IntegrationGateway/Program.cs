using Asp.Versioning;
using Microsoft.Extensions.Hosting.WindowsServices;
using WellSiteAutoPilot.Http;
using WellSiteAutoPilot.Http.ServiceCallers;
using WellSiteAutoPilot.IntegrationGateway.Adapters;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.PetroVisor;
using WellSiteAutoPilot.IntegrationGateway.Providers;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// The installer writes this service's caller verifiers here; development and CI supply them as configuration.
builder.Configuration.AddServiceCallerSettingsFile("gateway.keys.json");
builder.Services.AddServiceCallerAuthentication(builder.Configuration);
builder.Services.AddSingleton<AdapterCatalog>();
builder.Services.AddHttpClient("adapter-test");
builder.Services.AddHttpClient(AdapterConnectionTester.WindowsClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseDefaultCredentials = true, UseCookies = false });
builder.Services.AddSingleton<IPetroVisorSessionFactory>(
    PetroVisorKeycloakSessionFactory.IsConfigured(Environment.GetEnvironmentVariable)
        ? new PetroVisorKeycloakSessionFactory(Environment.GetEnvironmentVariable)
        : new PetroVisorPasswordSessionFactory(Environment.GetEnvironmentVariable));
builder.Services.AddTransient<AdapterConnectionTester>();
builder.Services.AddTransient<AdapterDiscoverer>();

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Weatherford.WellSiteAutoPilot.IntegrationGateway";
});
builder.Services.AddHealthChecks();
builder.Services.AddWellSiteTelemetry(builder.Configuration, "wellsite-autopilot-gateway");
builder.Services.AddWellSiteHttpErrorHandling();
builder.Services.AddEndpointsApiExplorer();
builder.Services
    .AddApiVersioning(options =>
    {
        options.ReportApiVersions = true;
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "WellSite AutoPilot Integration Gateway API",
        Version = "v1",
        Description = "Internal integration gateway contract."
    });
});
builder.Services.AddHttpClient("simulator-current-data", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Providers:Simulator:BaseUrl"] ??
        "http://127.0.0.1:5091");
    client.Timeout = TimeSpan.FromSeconds(5);
});
// Share breaker state without holding a factory-created HttpClient indefinitely.
builder.Services.AddTransient<SimulatorCurrentDataProvider>(services =>
    new SimulatorCurrentDataProvider(
        services.GetRequiredService<IHttpClientFactory>()
            .CreateClient("simulator-current-data")));
builder.Services.AddSingleton<CircuitBreakingCurrentDataProvider>(services =>
    new CircuitBreakingCurrentDataProvider(
        new FactoryCurrentDataProvider(() =>
            new SimulatorCurrentDataProvider(
                services.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("simulator-current-data"))),
        services.GetRequiredService<TimeProvider>(),
        builder.Configuration.GetValue("Providers:CircuitBreaker:FailureThreshold", 3),
        TimeSpan.FromSeconds(builder.Configuration.GetValue("Providers:CircuitBreaker:RecoverySeconds", 30))));
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddTransient<CurrentDataProviderRouter>(services =>
    new CurrentDataProviderRouter(
        [new KeyValuePair<string, ICurrentDataProvider>(
            "simulator", services.GetRequiredService<CircuitBreakingCurrentDataProvider>())],
        "simulator"));
builder.Services.AddTransient<IProviderHealthProvider>(
    serviceProvider =>
        serviceProvider.GetRequiredService<SimulatorCurrentDataProvider>());
builder.Services.AddWellSiteMessaging(
    builder.Configuration["Messaging:Nats:Url"] ?? "nats://127.0.0.1:4222");

var app = builder.Build();

app.UseWellSiteHttpErrorHandling();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Integration Gateway API v1");
    });
}

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

var internalApi = app.NewVersionedApi("WellSite AutoPilot Integration Gateway API");
var v1 = internalApi
    .MapGroup("/api/internal/v{version:apiVersion}")
    .HasApiVersion(1.0);

v1.MapGet(
    "/integrations",
    async (
        IProviderHealthProvider provider,
        CancellationToken cancellationToken) =>
    {
        var health = await provider.GetHealthAsync(cancellationToken);

        return Results.Ok(new[]
        {
            new
            {
                id = health.ProviderId,
                name = health.DisplayName,
                status = health.State.ToString(),
                reason = health.Reason
            }
        });
    })
    .WithName("ListIntegrations")
    .RequireAuthorization(ServiceCaller.DataReadPolicy);

v1.MapGet("/adapters", (AdapterCatalog catalog) => Results.Ok(catalog.List()))
    .WithName("ListAdapters")
    .RequireAuthorization(ServiceCaller.ServerPolicy)
    .Produces<IReadOnlyList<AdapterDescriptor>>(StatusCodes.Status200OK);

v1.MapPost(
    "/adapters/{adapterKey}/validate",
    (string adapterKey, AdapterSettingsRequest request, AdapterCatalog catalog) =>
        catalog.Find(adapterKey) is { } descriptor
            ? Results.Ok(new AdapterValidationResponse(AdapterSettingsValidator.Validate(
                descriptor, request.Settings ?? new Dictionary<string, string?>())))
            : Results.NotFound())
    .WithName("ValidateAdapterSettings")
    .RequireAuthorization(ServiceCaller.ServerPolicy)
    .Produces<AdapterValidationResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

v1.MapPost(
    "/adapters/{adapterKey}/test",
    async (string adapterKey, AdapterSettingsRequest request, AdapterCatalog catalog,
        AdapterConnectionTester tester, CancellationToken cancellationToken) =>
        catalog.Find(adapterKey) is { } descriptor
            ? Results.Ok(await tester.TestAsync(
                descriptor, request.Settings ?? new Dictionary<string, string?>(), cancellationToken))
            : Results.NotFound())
    .WithName("TestAdapterConnection")
    .RequireAuthorization(ServiceCaller.ServerPolicy)
    .Produces<AdapterTestResult>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

v1.MapPost(
    "/adapters/{adapterKey}/discover",
    async (string adapterKey, AdapterSettingsRequest request, AdapterCatalog catalog,
        AdapterDiscoverer discoverer, CancellationToken cancellationToken) =>
        catalog.Find(adapterKey) is { } descriptor && descriptor.CanDiscover
            ? Results.Ok(await discoverer.DiscoverAsync(
                descriptor, request.Settings ?? new Dictionary<string, string?>(), cancellationToken))
            : Results.NotFound())
    .WithName("DiscoverAdapterAssets")
    .RequireAuthorization(ServiceCaller.ServerPolicy)
    .Produces<DiscoveryResult>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

v1.MapGet(
    "/data/current",
    async (
        string assetExternalId,
        string quantity,
        string? providerId,
        CurrentDataProviderRouter provider,
        CancellationToken cancellationToken) =>
        Results.Ok(
            await provider.GetCurrentAsync(
                providerId,
                assetExternalId,
                quantity,
                cancellationToken)))
    .WithName("GetCurrentEngineeringValue")
    .RequireAuthorization(ServiceCaller.DataReadPolicy)
    .Produces<EngineeringValue>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest,
        "application/problem+json")
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status503ServiceUnavailable,
        "application/problem+json");

app.Run();

public partial class Program;
