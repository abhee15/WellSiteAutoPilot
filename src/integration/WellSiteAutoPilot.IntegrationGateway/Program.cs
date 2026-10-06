using Asp.Versioning;
using Microsoft.Extensions.Hosting.WindowsServices;
using WellSiteAutoPilot.Http;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.IntegrationGateway.Providers;
using WellSiteAutoPilot.Messaging.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Weatherford.WellSiteAutoPilot.IntegrationGateway";
});
builder.Services.AddHealthChecks();
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
builder.Services.AddHttpClient<SimulatorCurrentDataProvider>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Providers:Simulator:BaseUrl"] ??
        "http://127.0.0.1:5091");
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddTransient<ICurrentDataProvider>(
    serviceProvider =>
        serviceProvider.GetRequiredService<SimulatorCurrentDataProvider>());
builder.Services.AddTransient<IProviderHealthProvider>(
    serviceProvider =>
        serviceProvider.GetRequiredService<SimulatorCurrentDataProvider>());
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
        ICurrentDataProvider provider,
        CancellationToken cancellationToken) =>
        Results.Ok(
            await provider.GetCurrentAsync(
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

app.Run();

public partial class Program;
