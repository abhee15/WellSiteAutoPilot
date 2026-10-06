using Microsoft.Extensions.Hosting.WindowsServices;
using WellSiteAutoPilot.Messaging.Nats;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Weatherford.WellSiteAutoPilot.IntegrationGateway";
});
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
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

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.MapGet("/api/internal/v1/integrations", () => Results.Ok(Array.Empty<object>()))
    .WithName("ListIntegrations");

app.Run();

public partial class Program;
