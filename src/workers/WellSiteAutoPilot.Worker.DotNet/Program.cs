using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.Worker.DotNet.Execution;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Weatherford.WellSiteAutoPilot.Worker.DotNet";
});
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddHttpClient<GatewayDataClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Runtime:IntegrationGatewayUrl"] ??
        "http://127.0.0.1:5081");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddWellSiteMessaging(
    builder.Configuration["Messaging:Nats:Url"] ?? "nats://127.0.0.1:4222");
builder.Services.AddHostedService<ExecutionRequestConsumer>();

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready");

await app.RunAsync();
