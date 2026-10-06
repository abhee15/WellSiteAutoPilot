using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using WellSiteAutoPilot.Application.System;
using WellSiteAutoPilot.Infrastructure.System;

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

var app = builder.Build();

var swaggerEnabled = app.Environment.IsDevelopment() ||
                     app.Configuration.GetValue("Swagger:Enabled", false);

if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/api/v1/system/info", (IPlatformInformationService service) =>
    Results.Ok(new
    {
        product = "WellSite AutoPilot",
        components = service.GetComponents()
    }))
    .WithName("GetSystemInformation");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready");

app.Run();

public partial class Program;
