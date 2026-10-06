using Asp.Versioning;
using Microsoft.Extensions.Hosting.WindowsServices;
using WellSiteAutoPilot.Http;
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

v1.MapGet("/integrations", () => Results.Ok(Array.Empty<object>()))
    .WithName("ListIntegrations");

app.Run();

public partial class Program;
