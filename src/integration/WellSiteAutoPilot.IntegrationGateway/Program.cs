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
builder.Services.AddSingleton<ForeSiteConnectionMeasurements>();
builder.Services.AddSingleton<CygNetConnectionMeasurements>();
// CygNet connections reach real CygNet systems, one per connection like ForeSite; there is no simulated CygNet. The Gateway uses the
// CygNet API already installed on its server; a server without CygNet reports that on each CygNet connection.
builder.Services.AddSingleton<WellSiteAutoPilot.Integration.CygNet.Adapter.ICygNetClient>(_ =>
{
    // Optional override when CygNet is installed somewhere unusual: "CygNet:LibraryPath" in appsettings or CygNet__LibraryPath.
    WellSiteAutoPilot.Integration.CygNet.CygNetInstallLocator.ConfiguredFolder = builder.Configuration["CygNet:LibraryPath"];
    return OperatingSystem.IsWindows()
        ? new WellSiteAutoPilot.Integration.CygNet.CygNetApiClient()
        : new WellSiteAutoPilot.Integration.CygNet.Adapter.CygNetUnconfiguredClient();
});
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
// The command client is deliberately a plain client: no resilience or retry handler may ever be added to it, because a
// repeated send would apply a command twice. A test pins this.
builder.Services.AddHttpClient(SimulatorCommandProvider.ClientName, client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Providers:Simulator:BaseUrl"] ??
        "http://127.0.0.1:5091");
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddTransient<SimulatorCommandProvider>(services =>
    new SimulatorCommandProvider(
        services.GetRequiredService<IHttpClientFactory>().CreateClient(SimulatorCommandProvider.ClientName)));
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddTransient<CurrentDataProviderRouter>(services =>
{
    var providers = new List<KeyValuePair<string, ICurrentDataProvider>>
    {
        new KeyValuePair<string, ICurrentDataProvider>(
            "simulator", services.GetRequiredService<CircuitBreakingCurrentDataProvider>())
    };
    return new CurrentDataProviderRouter(providers, "simulator");
});
builder.Services.AddTransient<IProviderHealthProvider>(
    serviceProvider =>
        serviceProvider.GetRequiredService<SimulatorCurrentDataProvider>());
builder.Services.AddWellSiteMessaging(
    builder.Configuration["Messaging:Nats:Url"] ?? "nats://127.0.0.1:4222",
    builder.Configuration["Messaging:Nats:Token"]);

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
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = (context, report) => context.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString(),
        version = typeof(Program).Assembly.GetName().Version!.ToString()
    })
});

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

// Control commands. Server caller only: a worker, and therefore any module, can never reach these routes.
// Only the simulator carries commands; every other adapter answers 404 until its owner supplies a behaviour sheet.
v1.MapPost(
    "/adapters/{adapterKey}/command",
    async (string adapterKey, GatewayCommandRequest request, AdapterCatalog catalog,
        SimulatorCommandProvider simulator, CancellationToken cancellationToken) =>
        request is not null && CommandAdapter(catalog, adapterKey, request.Command) is not null
            ? Results.Ok(await simulator.SendAsync(request, cancellationToken))
            : Results.NotFound())
    .WithName("SendAdapterCommand")
    .RequireAuthorization(ServiceCaller.ServerPolicy)
    .Produces<GatewayCommandResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

v1.MapPost(
    "/adapters/{adapterKey}/command-readback",
    async (string adapterKey, ConnectionMeasurementRequest request, AdapterCatalog catalog,
        CurrentDataProviderRouter provider, CancellationToken cancellationToken) =>
        request is not null && ReadbackAdapter(catalog, adapterKey, request.Quantity)
            ? Results.Ok(await provider.GetCurrentAsync("simulator", request.ExternalId, request.Quantity, cancellationToken))
            : Results.NotFound())
    .WithName("ReadAdapterCommandValue")
    .RequireAuthorization(ServiceCaller.ServerPolicy)
    .Produces<EngineeringValue>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

v1.MapPost("/adapters/cygnet/current",
    async (ConnectionMeasurementRequest request, CygNetConnectionMeasurements provider, CancellationToken cancellationToken) =>
        Results.Ok(await provider.ReadAsync(request, cancellationToken)))
    .WithName("ReadAuthorizedCygNetMeasurement")
    .RequireAuthorization(ServiceCaller.ServerPolicy)
    .Produces<EngineeringValue>(StatusCodes.Status200OK);

v1.MapPost("/adapters/foresite/current",
    async (ConnectionMeasurementRequest request, ForeSiteConnectionMeasurements provider, CancellationToken cancellationToken) =>
        Results.Ok(await provider.ReadAsync(request, cancellationToken)))
    .WithName("ReadAuthorizedForeSiteMeasurement")
    .RequireAuthorization(ServiceCaller.ServerPolicy)
    .Produces<EngineeringValue>(StatusCodes.Status200OK);

v1.MapPost("/adapters/cygnet/history",
    async (ConnectionHistoryRequest request, CygNetConnectionMeasurements provider, CancellationToken cancellationToken) =>
        Results.Ok(await provider.ReadHistoryAsync(request, cancellationToken)))
    .WithName("ReadAuthorizedCygNetHistory")
    .RequireAuthorization(ServiceCaller.ServerPolicy)
    .Produces<HistoricalReadResult>(StatusCodes.Status200OK);

// Server-only typed history boundary; no worker or public explorer execution endpoint.
v1.MapPost("/adapters/foresite/configuration",
    async (ConnectionConfigurationRequest request, ForeSiteConnectionMeasurements provider, CancellationToken cancellationToken) =>
        Results.Ok(await provider.ReadConfigurationAsync(request, cancellationToken)))
    .WithName("ReadAuthorizedForeSiteConfiguration")
    .RequireAuthorization(ServiceCaller.ServerPolicy)
    .Produces<AssetConfigurationResult>(StatusCodes.Status200OK);

v1.MapPost("/adapters/foresite/history",
    async (ConnectionHistoryRequest request, ForeSiteConnectionMeasurements provider, CancellationToken cancellationToken) =>
        Results.Ok(await provider.ReadHistoryAsync(request, cancellationToken)))
    .WithName("ReadAuthorizedForeSiteHistory")
    .RequireAuthorization(ServiceCaller.ServerPolicy)
    .Produces<HistoricalReadResult>(StatusCodes.Status200OK);

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

// Only the simulator adapter carries commands so far. A command name the adapter does not define is never sent.
static AdapterCommandDefinition? CommandAdapter(AdapterCatalog catalog, string adapterKey, string? command) =>
    string.Equals(adapterKey, "simulator", StringComparison.Ordinal)
        ? catalog.Find(adapterKey)?.Commands?.FirstOrDefault(item => string.Equals(item.Name, command, StringComparison.Ordinal))
        : null;

// Only a quantity that one of the adapter's commands names as its read-back can be read through this route.
static bool ReadbackAdapter(AdapterCatalog catalog, string adapterKey, string? quantity) =>
    string.Equals(adapterKey, "simulator", StringComparison.Ordinal) &&
    (catalog.Find(adapterKey)?.Commands?.Any(item => string.Equals(item.ReadbackQuantity, quantity, StringComparison.Ordinal)) ?? false);

public partial class Program;
