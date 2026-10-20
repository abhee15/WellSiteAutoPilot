using Asp.Versioning;
using Microsoft.Extensions.Hosting.WindowsServices;
using WellSiteAutoPilot.Http;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.IntegrationGateway.Connections;
using WellSiteAutoPilot.IntegrationGateway.Providers;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.Telemetry;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddHttpClient("connection-probe", client => client.Timeout = TimeSpan.FromSeconds(45))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseDefaultCredentials = true });
builder.Services.AddSingleton<AdapterRegistry>();
builder.Services.AddSingleton(services => new ConnectionStore(
    builder.Configuration["Connections:StorePath"] ??
        Path.Combine(AppContext.BaseDirectory, "connections.json"),
    services.GetRequiredService<TimeProvider>()));
builder.Services.AddWellSiteMessaging(
    builder.Configuration["Messaging:Nats:Url"] ?? "nats://127.0.0.1:4222");

var app = builder.Build();

app.UseWellSiteHttpErrorHandling();

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
    .WithName("ListIntegrations");

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
    .Produces<EngineeringValue>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest,
        "application/problem+json")
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status503ServiceUnavailable,
        "application/problem+json");

v1.MapGet("/adapters", (AdapterRegistry registry) => Results.Ok(registry.List()))
    .WithName("ListAdapters");

v1.MapGet("/connections", (ConnectionStore store) => Results.Ok(store.List()))
    .WithName("ListConnections");

v1.MapPost("/connections", (ConnectionInput input, ConnectionStore store, AdapterRegistry registry) =>
{
    if (ValidateConnection(input, registry) is { } problem) return problem;
    return Results.Created("/connections", store.Create(input));
}).WithName("CreateConnection");

v1.MapPut("/connections/{id:guid}", (Guid id, ConnectionInput input, ConnectionStore store, AdapterRegistry registry) =>
{
    if (ValidateConnection(input, registry) is { } problem) return problem;
    try
    {
        return store.Update(id, input) is { } updated ? Results.Ok(updated) : Results.NotFound();
    }
    catch (StaleRevisionException)
    {
        return Results.Conflict(new { error = "STALE_REVISION", message = "The connection changed since it was loaded." });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = "INVALID", message = ex.Message });
    }
}).WithName("UpdateConnection");

v1.MapDelete("/connections/{id:guid}", (Guid id, ConnectionStore store) =>
    store.Delete(id) ? Results.NoContent() : Results.NotFound())
    .WithName("DeleteConnection");

v1.MapPost("/connections/{id:guid}/test", async (
    Guid id, ConnectionStore store, AdapterRegistry registry, TimeProvider time, CancellationToken ct) =>
{
    if (store.Get(id) is not { } connection) return Results.NotFound();
    if (!connection.Enabled)
        return Results.Conflict(new { error = "DISABLED", message = "The connection is disabled." });
    var (health, metadata, discovery) = registry.Build(connection);
    var h = await health.GetHealthAsync(ct);
    var compat = metadata is null ? null : await metadata.GetCompatibilityAsync(ct);
    var result = new ConnectionTestResult(connection.Revision, time.GetUtcNow(), h.State.ToString(), h.Reason,
        compat?.State.ToString() ?? "Unknown", compat?.Reason, discovery is not null);
    // A test that finished after an edit is dropped, never attached to the newer revision.
    return store.RecordTest(id, result) is { } saved
        ? Results.Ok(saved)
        : Results.Conflict(new { error = "STALE_REVISION", message = "The connection changed while testing." });
}).WithName("TestConnection");

v1.MapPost("/connections/{id:guid}/discover", async (
    Guid id, ConnectionStore store, AdapterRegistry registry, TimeProvider time, CancellationToken ct) =>
{
    if (store.Get(id) is not { } connection) return Results.NotFound();
    if (!connection.Enabled)
        return Results.Conflict(new { error = "DISABLED", message = "The connection is disabled." });
    var (_, _, discovery) = registry.Build(connection);
    if (discovery is null)
        return Results.BadRequest(new { error = "NO_DISCOVERY", message = "This adapter does not support asset discovery." });
    var assets = new List<DiscoveredAsset>();
    string? token = null;
    do
    {
        var page = await discovery.DiscoverAssetsAsync(token, ct);
        assets.AddRange(page);
        token = null; // the contract returns no next-page token yet (gap recorded in the POC findings)
    }
    while (token is not null);
    return Results.Ok(new DiscoveryResult(id, time.GetUtcNow(), assets));
}).WithName("DiscoverConnectionAssets");

static IResult? ValidateConnection(ConnectionInput input, AdapterRegistry registry)
{
    if (string.IsNullOrWhiteSpace(input.Name))
        return Results.BadRequest(new { error = "INVALID", message = "A name is required." });
    if (registry.Find(input.AdapterKey) is null)
        return Results.BadRequest(new { error = "INVALID", message = "Unknown adapter." });
    if (!Uri.TryCreate(input.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        return Results.BadRequest(new { error = "INVALID", message = "The endpoint must be an http(s) URL." });
    return null;
}

app.Run();

public partial class Program;
