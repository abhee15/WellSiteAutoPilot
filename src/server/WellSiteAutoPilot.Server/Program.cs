using Asp.Versioning;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using WellSiteAutoPilot.Api.Contracts.Assets;
using WellSiteAutoPilot.Api.Contracts.System;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.System;
using WellSiteAutoPilot.Http;
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
            ToResponse(assetType));
    })
    .WithName("CreateAssetType")
    .Produces<AssetTypeResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapGet(
    "/asset-types",
    async (AssetService assetService, CancellationToken cancellationToken) =>
        (await assetService.ListAssetTypesAsync(cancellationToken))
            .Select(ToResponse)
            .ToArray())
    .WithName("ListAssetTypes")
    .Produces<AssetTypeResponse[]>(StatusCodes.Status200OK);

v1.MapGet(
    "/asset-types/{assetTypeId:guid}",
    async (
        Guid assetTypeId,
        AssetService assetService,
        CancellationToken cancellationToken) =>
        ToResponse(await assetService.GetRequiredAssetTypeAsync(assetTypeId, cancellationToken)))
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
            ToResponse(asset));
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
        .Select(ToResponse)
        .ToArray())
    .WithName("ListAssets")
    .Produces<AssetResponse[]>(StatusCodes.Status200OK);

v1.MapGet(
    "/assets/{assetId:guid}",
    async (
        Guid assetId,
        AssetService assetService,
        CancellationToken cancellationToken) =>
        ToResponse(await assetService.GetRequiredAssetAsync(assetId, cancellationToken)))
    .WithName("GetAsset")
    .Produces<AssetResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready");

app.Run();

static AssetTypeResponse ToResponse(WellSiteAutoPilot.Domain.Assets.AssetTypeDefinition assetType) => new(
    assetType.Id,
    assetType.Key,
    assetType.DisplayName,
    assetType.SchemaVersion,
    assetType.AttributeSchemaJson,
    assetType.IsActive,
    assetType.CreatedAtUtc);

static AssetResponse ToResponse(WellSiteAutoPilot.Domain.Assets.Asset asset) => new(
    asset.Id,
    asset.AssetTypeId,
    asset.Name,
    asset.ParentAssetId,
    asset.AttributeValuesJson,
    asset.IsActive,
    asset.CreatedAtUtc);

public partial class Program;
