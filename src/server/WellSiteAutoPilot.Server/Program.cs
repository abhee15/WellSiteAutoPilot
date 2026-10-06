using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using WellSiteAutoPilot.Api.Contracts.System;
using WellSiteAutoPilot.Application.System;
using WellSiteAutoPilot.Infrastructure.System;
using WellSiteAutoPilot.Messaging.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "WellSite AutoPilot API",
        Version = "v1",
        Description = "HTTP API for WellSite AutoPilot."
    });
});
builder.Services.AddSingleton<IPlatformInformationService, PlatformInformationService>();
builder.Services.AddWellSiteMessaging(
    builder.Configuration["Messaging:Nats:Url"] ?? "nats://127.0.0.1:4222");

var app = builder.Build();

var swaggerEnabled = app.Environment.IsDevelopment() ||
                     app.Configuration.GetValue("Swagger:Enabled", false);

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/api/v1/system/info", (IPlatformInformationService service) =>
    new SystemInfoResponse(
        "WellSite AutoPilot",
        service.GetComponents()
            .Select(component => new PlatformComponentResponse(
                component.Name,
                component.Version,
                component.Status.ToString()))
            .ToArray()))
    .WithName("GetSystemInformation")
    .Produces<SystemInfoResponse>(StatusCodes.Status200OK);

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready");

app.Run();

public partial class Program;
