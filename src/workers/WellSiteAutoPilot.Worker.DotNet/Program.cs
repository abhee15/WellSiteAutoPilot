using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using WellSiteAutoPilot.Http.ServiceCallers;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.Telemetry;
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
builder.Services.AddWellSiteTelemetry(builder.Configuration, "wellsite-autopilot-worker-dotnet");
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<ILogicModuleResolver, PackageLogicModuleResolver>();
builder.Services.AddTransient<ModuleExecutionEngine>(services =>
    new ModuleExecutionEngine(
        services.GetRequiredService<GatewayDataClient>(),
        services.GetRequiredService<ILogicModuleResolver>(),
        builder.Configuration.GetValue("Execution:MaximumConcurrentInputReads", 8)));
builder.Configuration.AddServiceCallerSettingsFile("worker.keys.json");
builder.Services.AddHttpClient<GatewayDataClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Runtime:IntegrationGatewayUrl"] ??
        "http://127.0.0.1:5081");
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddServiceCallerKey(builder.Configuration, ServiceCaller.Worker);
builder.Services.AddWellSiteMessaging(
    builder.Configuration["Messaging:Nats:Url"] ?? "nats://127.0.0.1:4222");
builder.Services.AddSingleton(new JetStreamConsumerSettings(
    builder.Configuration.GetValue("Messaging:JetStream:RequestAckWaitSeconds", 120),
    builder.Configuration.GetValue("Messaging:JetStream:ResultAckWaitSeconds", 30),
    builder.Configuration.GetValue("Messaging:JetStream:RequestProgressSeconds", 20),
    builder.Configuration.GetValue("Messaging:JetStream:MaxDeliver", 5)).Validate());
builder.Services.AddHostedService<ExecutionRequestConsumer>();
builder.Services.AddHostedService<ExecutionRequestV2Consumer>();

var app = builder.Build();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready");

await app.RunAsync();
