using Asp.Versioning;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using WellSiteAutoPilot.Api.Contracts.Assets;
using WellSiteAutoPilot.Api.Contracts.Executions;
using WellSiteAutoPilot.Api.Contracts.ConfiguredLogic;
using WellSiteAutoPilot.Api.Contracts.System;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.System;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
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
builder.Services.AddScoped<AssetService>();
builder.Services.AddScoped<ExecutionService>();
builder.Services.AddScoped<ConfiguredLogicService>();
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
    "/asset-types",
    async (
        CreateAssetTypeRequest request,
        AssetService assetService,
        CancellationToken cancellationToken) =>
    {
        var assetType = await assetService.CreateAssetTypeAsync(
            new CreateAssetTypeCommand(
                request.Key,
                request.DisplayName,
                request.AttributeSchemaJson),
            cancellationToken);

        return Results.Created(
            $"/api/v1/asset-types/{assetType.Id}",
            ToAssetTypeResponse(assetType));
    })
    .WithName("CreateAssetType")
    .Produces<AssetTypeResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapGet(
    "/asset-types",
    async (AssetService assetService, CancellationToken cancellationToken) =>
        (await assetService.ListAssetTypesAsync(cancellationToken))
            .Select(ToAssetTypeResponse)
            .ToArray())
    .WithName("ListAssetTypes")
    .Produces<AssetTypeResponse[]>(StatusCodes.Status200OK);

v1.MapGet(
    "/asset-types/{assetTypeId:guid}",
    async (
        Guid assetTypeId,
        AssetService assetService,
        CancellationToken cancellationToken) =>
        ToAssetTypeResponse(
            await assetService.GetRequiredAssetTypeAsync(assetTypeId, cancellationToken)))
    .WithName("GetAssetType")
    .Produces<AssetTypeResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapPost(
    "/assets",
    async (
        CreateAssetRequest request,
        AssetService assetService,
        CancellationToken cancellationToken) =>
    {
        var asset = await assetService.CreateAssetAsync(
            new CreateAssetCommand(
                request.AssetTypeId,
                request.Name,
                request.ParentAssetId,
                request.AttributeValuesJson),
            cancellationToken);

        return Results.Created(
            $"/api/v1/assets/{asset.Id}",
            ToAssetResponse(asset));
    })
    .WithName("CreateAsset")
    .Produces<AssetResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapGet(
    "/assets",
    async (
        Guid? assetTypeId,
        Guid? parentAssetId,
        int? limit,
        AssetService assetService,
        CancellationToken cancellationToken) =>
        (await assetService.ListAssetsAsync(
            assetTypeId,
            parentAssetId,
            limit ?? 100,
            cancellationToken))
        .Select(ToAssetResponse)
        .ToArray())
    .WithName("ListAssets")
    .Produces<AssetResponse[]>(StatusCodes.Status200OK);

v1.MapGet(
    "/assets/{assetId:guid}",
    async (
        Guid assetId,
        AssetService assetService,
        CancellationToken cancellationToken) =>
        ToAssetResponse(await assetService.GetRequiredAssetAsync(assetId, cancellationToken)))
    .WithName("GetAsset")
    .Produces<AssetResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapPost(
    "/configured-logic",
    async (
        CreateConfiguredLogicRequest request,
        ConfiguredLogicService service,
        CancellationToken cancellationToken) =>
    {
        var created = await service.CreateAsync(
            new CreateConfiguredLogicCommand(
                request.Name,
                request.ModuleManifestJson,
                request.ParametersJson,
                request.AssetBindings
                    .Select(binding => new ConfiguredLogicAssetBindingCommand(
                        binding.Role,
                        binding.AssetId,
                        binding.ParameterOverridesJson))
                    .ToArray(),
                request.DataBindings
                    .Select(binding => new ConfiguredLogicDataBindingCommand(
                        binding.RequirementId,
                        binding.AssetId,
                        binding.ProviderId,
                        binding.ProviderAssetExternalId,
                        binding.ProviderMappingJson))
                    .ToArray(),
                request.Schedule is null
                    ? null
                    : new ConfiguredLogicScheduleCommand(
                        request.Schedule.Enabled,
                        request.Schedule.CadenceSeconds,
                        request.Schedule.StartAtUtc,
                        request.Schedule.TimeZoneId)),
            cancellationToken);

        return Results.Created(
            $"/api/v1/configured-logic/{created.Id}",
            ToConfiguredLogicResponse(created));
    })
    .WithName("CreateConfiguredLogic")
    .Produces<ConfiguredLogicResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapGet(
    "/configured-logic",
    async (
        int? limit,
        ConfiguredLogicService service,
        CancellationToken cancellationToken) =>
        (await service.ListAsync(limit ?? 100, cancellationToken))
            .Select(ToConfiguredLogicResponse)
            .ToArray())
    .WithName("ListConfiguredLogic")
    .Produces<ConfiguredLogicResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

v1.MapGet(
    "/configured-logic/{configuredLogicId:guid}",
    async (
        Guid configuredLogicId,
        ConfiguredLogicService service,
        CancellationToken cancellationToken) =>
        ToConfiguredLogicResponse(
            await service.GetRequiredAsync(configuredLogicId, cancellationToken)))
    .WithName("GetConfiguredLogic")
    .Produces<ConfiguredLogicResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapPost(
    "/configured-logic/{configuredLogicId:guid}/revisions",
    async (
        Guid configuredLogicId,
        CreateConfiguredLogicRevisionRequest request,
        ConfiguredLogicService service,
        CancellationToken cancellationToken) =>
    {
        var revision = await service.CreateRevisionAsync(
            configuredLogicId,
            new CreateConfiguredLogicRevisionCommand(
                request.ModuleManifestJson,
                request.ParametersJson,
                request.AssetBindings
                    .Select(binding => new ConfiguredLogicAssetBindingCommand(
                        binding.Role,
                        binding.AssetId,
                        binding.ParameterOverridesJson))
                    .ToArray(),
                request.DataBindings
                    .Select(binding => new ConfiguredLogicDataBindingCommand(
                        binding.RequirementId,
                        binding.AssetId,
                        binding.ProviderId,
                        binding.ProviderAssetExternalId,
                        binding.ProviderMappingJson))
                    .ToArray(),
                request.Schedule is null
                    ? null
                    : new ConfiguredLogicScheduleCommand(
                        request.Schedule.Enabled,
                        request.Schedule.CadenceSeconds,
                        request.Schedule.StartAtUtc,
                        request.Schedule.TimeZoneId)),
            cancellationToken);

        return Results.Created(
            $"/api/v1/configured-logic/{configuredLogicId}/revisions/{revision.Id}",
            ToConfiguredLogicRevisionResponse(revision));
    })
    .WithName("CreateConfiguredLogicRevision")
    .Produces<ConfiguredLogicRevisionResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapPost(
    "/configured-logic/{configuredLogicId:guid}/revisions/{revisionId:guid}/validate",
    async (
        Guid configuredLogicId,
        Guid revisionId,
        ConfiguredLogicService service,
        CancellationToken cancellationToken) =>
        ToConfiguredLogicRevisionResponse(
            await service.ValidateRevisionAsync(
                configuredLogicId,
                revisionId,
                cancellationToken)))
    .WithName("ValidateConfiguredLogicRevision")
    .Produces<ConfiguredLogicRevisionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapPost(
    "/configured-logic/{configuredLogicId:guid}/revisions/{revisionId:guid}/activate",
    async (
        Guid configuredLogicId,
        Guid revisionId,
        ConfiguredLogicService service,
        CancellationToken cancellationToken) =>
        ToConfiguredLogicRevisionResponse(
            await service.ActivateRevisionAsync(
                configuredLogicId,
                revisionId,
                cancellationToken)))
    .WithName("ActivateConfiguredLogicRevision")
    .Produces<ConfiguredLogicRevisionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapGet(
    "/executions",
    async (
        string? status,
        int? limit,
        ExecutionService executionService,
        CancellationToken cancellationToken) =>
        (await executionService.ListAsync(
            status,
            limit ?? 50,
            cancellationToken))
        .Select(ToResponse)
        .ToArray())
    .WithName("ListExecutions")
    .Produces<ExecutionResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest,
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

static AssetTypeResponse ToAssetTypeResponse(WellSiteAutoPilot.Domain.Assets.AssetTypeDefinition assetType) => new(
    assetType.Id,
    assetType.Key,
    assetType.DisplayName,
    assetType.SchemaVersion,
    assetType.AttributeSchemaJson,
    assetType.IsActive,
    assetType.CreatedAtUtc);

static AssetResponse ToAssetResponse(WellSiteAutoPilot.Domain.Assets.Asset asset) => new(
    asset.Id,
    asset.AssetTypeId,
    asset.Name,
    asset.ParentAssetId,
    asset.AttributeValuesJson,
    asset.IsActive,
    asset.CreatedAtUtc);

static ConfiguredLogicResponse ToConfiguredLogicResponse(ConfiguredLogicDefinition configuredLogic) => new(
    configuredLogic.Id,
    configuredLogic.Name,
    configuredLogic.ActiveRevisionId,
    configuredLogic.CreatedAtUtc,
    configuredLogic.Revisions
        .Select(ToConfiguredLogicRevisionResponse)
        .ToArray());

static ConfiguredLogicRevisionResponse ToConfiguredLogicRevisionResponse(ConfiguredLogicRevision revision) => new(
    revision.Id,
    revision.RevisionNumber,
    revision.ModuleId,
    revision.ModuleVersion,
    revision.ModuleManifestJson,
    revision.Mode.ToString(),
    revision.ParametersJson,
    revision.Status.ToString(),
    revision.CreatedAtUtc,
    revision.ValidatedAtUtc,
    revision.ActivatedAtUtc,
    revision.Schedule is null
        ? null
        : new ConfiguredLogicScheduleResponse(
            revision.Schedule.Enabled,
            revision.Schedule.CadenceSeconds,
            revision.Schedule.StartAtUtc,
            revision.Schedule.TimeZoneId),
    revision.AssetBindings
        .Select(binding => new ConfiguredLogicAssetBindingResponse(
            binding.Role,
            binding.AssetId,
            binding.ParameterOverridesJson))
        .ToArray(),
    revision.DataBindings
        .Select(binding => new ConfiguredLogicDataBindingResponse(
            binding.RequirementId,
            binding.AssetId,
            binding.ProviderId,
            binding.ProviderAssetExternalId,
            binding.ProviderMappingJson))
        .ToArray());

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
