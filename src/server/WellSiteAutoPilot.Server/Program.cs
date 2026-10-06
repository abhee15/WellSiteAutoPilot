using Asp.Versioning;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using WellSiteAutoPilot.Api.Contracts.Executions;
using WellSiteAutoPilot.Api.Contracts.System;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.System;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Http;
using WellSiteAutoPilot.Infrastructure.Messaging;
using WellSiteAutoPilot.Infrastructure.System;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.Persistence;

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
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<ExecutionService>();
builder.Services.AddScoped<OutboxPublisher>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<ExecutionResultConsumer>();
builder.Services.AddWellSitePersistence(
    builder.Configuration.GetConnectionString("WellSiteAutoPilot") ??
    Environment.GetEnvironmentVariable("WSA_DATABASE_CONNECTION_STRING") ??
    "Host=127.0.0.1;Port=5432;Database=wellsite_autopilot;Username=wsa");
builder.Services.AddWellSiteMessaging(
    builder.Configuration["Messaging:Nats:Url"] ?? "nats://127.0.0.1:4222");

var app = builder.Build();

app.UseWellSiteHttpErrorHandling();

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
    .Produces<SystemInfoResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status500InternalServerError,
        "application/problem+json");

v1.MapPost(
    "/executions/shadow",
    async (
        RequestShadowExecutionRequest request,
        ExecutionService executionService,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        var execution = await executionService.RequestShadowAsync(
            new ShadowExecutionCommand(
                request.LogicInstanceId,
                request.ModuleId,
                request.ModuleVersion,
                request.ConfigurationRevisionId,
                request.AssetId,
                request.AssetExternalId,
                request.Quantity),
            CorrelationContext.GetCorrelationId(httpContext),
            cancellationToken);

        return Results.Created(
            $"/api/v1/executions/{execution.Id}",
            ToResponse(execution));
    })
    .WithName("RequestShadowExecution")
    .Produces<ExecutionResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest,
        "application/problem+json")
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status500InternalServerError,
        "application/problem+json");

v1.MapGet(
    "/executions/{executionId:guid}",
    async (
        Guid executionId,
        ExecutionService executionService,
        CancellationToken cancellationToken) =>
        ToResponse(await executionService.GetRequiredAsync(executionId, cancellationToken)))
    .WithName("GetExecution")
    .Produces<ExecutionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status404NotFound,
        "application/problem+json")
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status500InternalServerError,
        "application/problem+json");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready");

app.Run();

static ExecutionResponse ToResponse(ExecutionRecord execution) => new(
    execution.Id,
    execution.LogicInstanceId,
    execution.ModuleId,
    execution.ModuleVersion,
    execution.ConfigurationRevisionId,
    execution.AssetId,
    execution.AssetExternalId,
    execution.Quantity,
    execution.Mode.ToString(),
    execution.Status.ToString(),
    execution.CorrelationId,
    execution.RequestedAtUtc,
    execution.StartedAtUtc,
    execution.CompletedAtUtc,
    execution.ResultCode,
    execution.FailureCode,
    execution.OutputJson);

public partial class Program;
