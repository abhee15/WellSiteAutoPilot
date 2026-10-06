using Asp.Versioning;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using WellSiteAutoPilot.Api.Contracts.System;
using WellSiteAutoPilot.Application.System;
using WellSiteAutoPilot.Infrastructure.System;
using WellSiteAutoPilot.Messaging.Nats;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
});

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Weatherford.WellSiteAutoPilot.Server";
});
builder.Services.AddHealthChecks();
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
        Title = "WellSite AutoPilot API",
        Version = "v1",
        Description = "HTTP API for WellSite AutoPilot."
    });
});
builder.Services.AddHttpClient("PlatformHealth", client =>
{
    client.Timeout = TimeSpan.FromSeconds(2);
});
builder.Services.AddSingleton<IPlatformInformationService>(serviceProvider =>
{
    var clientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var client = clientFactory.CreateClient("PlatformHealth");

    return new PlatformInformationService(
        client,
        builder.Configuration["Runtime:IntegrationGatewayUrl"] ?? "http://127.0.0.1:5081",
        builder.Configuration["Runtime:DotNetWorkerUrl"] ?? "http://127.0.0.1:5082");
});
builder.Services.AddWellSiteMessaging(
    builder.Configuration["Messaging:Nats:Url"] ?? "nats://127.0.0.1:4222");

var app = builder.Build();

var swaggerEnabled = app.Environment.IsDevelopment() ||
                     app.Configuration.GetValue("Swagger:Enabled", false);

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "WellSite AutoPilot API v1");
    });
}

app.UseDefaultFiles();
app.UseStaticFiles();

var productApi = app.NewVersionedApi("WellSite AutoPilot API");
var v1 = productApi
    .MapGroup("/api/v{version:apiVersion}")
    .HasApiVersion(1.0);

v1.MapGet(
    "/system/info",
    async (IPlatformInformationService service, CancellationToken cancellationToken) =>
    {
        var components = await service.GetComponentsAsync(cancellationToken);

        return new SystemInfoResponse(
            "WellSite AutoPilot",
            components
                .Select(component => new PlatformComponentResponse(
                    component.Name,
                    component.Version,
                    component.Status.ToString()))
                .ToArray());
    })
    .WithName("GetSystemInformation")
    .Produces<SystemInfoResponse>(StatusCodes.Status200OK);

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready");

app.Run();

public partial class Program;
