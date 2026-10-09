using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using WellSiteAutoPilot.Api.Contracts.Assets;
using WellSiteAutoPilot.Api.Contracts.Executions;
using WellSiteAutoPilot.Api.Contracts.Logic;
using WellSiteAutoPilot.Api.Contracts.ConfiguredLogic;
using WellSiteAutoPilot.Api.Contracts.System;
using WellSiteAutoPilot.Api.Contracts.Security;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.System;
using WellSiteAutoPilot.Application.Scheduling;
using WellSiteAutoPilot.Application.Recommendations;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Http;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Infrastructure.Messaging;
using WellSiteAutoPilot.Infrastructure.System;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Server.Scheduling;
using WellSiteAutoPilot.Server.Security;

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
builder.Services.Configure<SecurityOptions>(
    builder.Configuration.GetSection(SecurityOptions.SectionName));

var authenticationMode =
    builder.Configuration[$"{SecurityOptions.SectionName}:AuthenticationMode"] ??
    SecurityOptions.NegotiateAuthenticationMode;

if (string.Equals(
        authenticationMode,
        SecurityOptions.TestHeaderAuthenticationMode,
        StringComparison.OrdinalIgnoreCase))
{
    if (!builder.Environment.IsDevelopment() &&
        !builder.Environment.IsEnvironment("CI"))
    {
        throw new InvalidOperationException(
            "TestHeader authentication is permitted only in Development or CI.");
    }

    builder.Services
        .AddAuthentication(TestHeaderAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, TestHeaderAuthenticationHandler>(
            TestHeaderAuthenticationHandler.SchemeName,
            _ => { });
}
else if (string.Equals(
             authenticationMode,
             SecurityOptions.NegotiateAuthenticationMode,
             StringComparison.OrdinalIgnoreCase))
{
    builder.Services
        .AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();
}
else
{
    throw new InvalidOperationException(
        $"Unsupported Security:AuthenticationMode '{authenticationMode}'.");
}

builder.Services.AddAuthorization(WellSitePolicies.AddPolicies);

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
builder.Services.AddScoped<LogicModuleCatalogService>();
builder.Services.AddScoped<ConfiguredLogicService>();
builder.Services.AddScoped<ScheduledShadowSchedulerService>();
builder.Services.AddScoped<RecommendationMaterializer>();
builder.Services.AddScoped<UserAccessService>();
builder.Services.Configure<ScheduledShadowSchedulerOptions>(
    builder.Configuration.GetSection(ScheduledShadowSchedulerOptions.SectionName));
builder.Services.AddScoped<OutboxPublisher>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<ExecutionResultConsumer>();
builder.Services.AddHostedService<ExecutionResultV2Consumer>();
builder.Services.AddHostedService<ScheduledShadowSchedulerHostedService>();
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
app.UseAuthentication();
app.UseMiddleware<UserAccessClaimsMiddleware>();
app.UseAuthorization();

var productApi = app.NewVersionedApi("WellSite AutoPilot API");
var v1 = productApi
    .MapGroup("/api/v{version:apiVersion}")
    .HasApiVersion(1.0)
    .RequireAuthorization();

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

v1.MapGet(
    "/security/me",
    async (
        HttpContext httpContext,
        UserAccessService userAccessService,
        CancellationToken cancellationToken) =>
    {
        var user = await userAccessService.GetRequiredAsync(
            GetCurrentUserId(httpContext.User),
            cancellationToken);

        return new CurrentUserResponse(
            user.Id,
            user.IdentityName,
            user.DisplayName,
            user.IsActive,
            user.Roles.Select(item => item.ToString()).ToArray(),
            user.AssetScopeIds.ToArray());
    })
    .WithName("GetCurrentUser")
    .Produces<CurrentUserResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized);

v1.MapGet(
    "/security/users",
    async (
        int? limit,
        UserAccessService userAccessService,
        CancellationToken cancellationToken) =>
        (await userAccessService.ListAsync(
            limit ?? 100,
            cancellationToken))
        .Select(ToSecurityUserResponse)
        .ToArray())
    .WithName("ListSecurityUsers")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<SecurityUserResponse[]>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status403Forbidden);

v1.MapPut(
    "/security/users/{userId:guid}/access",
    async (
        Guid userId,
        ReplaceUserAccessRequest request,
        UserAccessService userAccessService,
        CancellationToken cancellationToken) =>
    {
        var roles = ParseApplicationRoles(request.Roles);
        var updated = await userAccessService.ReplaceAccessAsync(
            userId,
            roles,
            request.AssetScopeIds ?? [],
            cancellationToken);

        return ToSecurityUserResponse(updated);
    })
    .WithName("ReplaceSecurityUserAccess")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<SecurityUserResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest,
        "application/problem+json")
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status404NotFound,
        "application/problem+json")
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status409Conflict,
        "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

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
    .RequireAuthorization(WellSitePolicies.AssetsManage)
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
    .RequireAuthorization(WellSitePolicies.AssetsRead)
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
    .RequireAuthorization(WellSitePolicies.AssetsRead)
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
    .RequireAuthorization(WellSitePolicies.AssetsManage)
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
    .RequireAuthorization(WellSitePolicies.AssetsRead)
    .Produces<AssetResponse[]>(StatusCodes.Status200OK);

v1.MapGet(
    "/assets/{assetId:guid}",
    async (
        Guid assetId,
        AssetService assetService,
        CancellationToken cancellationToken) =>
        ToAssetResponse(await assetService.GetRequiredAssetAsync(assetId, cancellationToken)))
    .WithName("GetAsset")
    .RequireAuthorization(WellSitePolicies.AssetsRead)
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
    .RequireAuthorization(WellSitePolicies.ConfiguredLogicManage)
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
    .RequireAuthorization(WellSitePolicies.ConfiguredLogicRead)
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
    .RequireAuthorization(WellSitePolicies.ConfiguredLogicRead)
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
    .RequireAuthorization(WellSitePolicies.ConfiguredLogicManage)
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
    .RequireAuthorization(WellSitePolicies.ConfiguredLogicManage)
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
    .RequireAuthorization(WellSitePolicies.ConfiguredLogicManage)
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
    .RequireAuthorization(WellSitePolicies.ExecutionsRead)
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
    .RequireAuthorization(WellSitePolicies.ExecutionsRequest)
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
    .RequireAuthorization(WellSitePolicies.ExecutionsRead)
    .Produces<ExecutionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status404NotFound,
        "application/problem+json")
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status500InternalServerError,
        "application/problem+json");

v1.MapPost(
    "/logic-modules",
    async (
        RegisterLogicModuleRequest request,
        LogicModuleCatalogService catalog,
        CancellationToken cancellationToken) =>
    {
        var module = await catalog.RegisterAsync(
            new RegisterLogicModuleCommand(
                request.ManifestJson,
                request.PackageSha256,
                WellSiteAutoPilot.Domain.Logic.LogicModuleTrustStatus.Untrusted),
            cancellationToken);

        return Results.Created(
            $"/api/v1/logic-modules/{Uri.EscapeDataString(module.ModuleId)}/{Uri.EscapeDataString(module.Version)}",
            ToLogicModuleResponse(module));
    })
    .WithName("RegisterLogicModule")
    .RequireAuthorization(WellSitePolicies.LogicManage)
    .Produces<LogicModuleResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapGet(
    "/logic-modules",
    async (
        string? moduleId,
        int? limit,
        LogicModuleCatalogService catalog,
        CancellationToken cancellationToken) =>
        (await catalog.ListAsync(moduleId, limit ?? 100, cancellationToken))
            .Select(ToLogicModuleResponse)
            .ToArray())
    .WithName("ListLogicModules")
    .RequireAuthorization(WellSitePolicies.LogicRead)
    .Produces<LogicModuleResponse[]>(StatusCodes.Status200OK);

v1.MapGet(
    "/logic-modules/{moduleId}/{moduleVersion}",
    async (
        string moduleId,
        string moduleVersion,
        LogicModuleCatalogService catalog,
        CancellationToken cancellationToken) =>
        ToLogicModuleResponse(
            await catalog.GetRequiredAsync(moduleId, moduleVersion, cancellationToken)))
    .WithName("GetLogicModule")
    .RequireAuthorization(WellSitePolicies.LogicRead)
    .Produces<LogicModuleResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

app.MapHealthChecks("/health/ready").AllowAnonymous();

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

static LogicModuleResponse ToLogicModuleResponse(
    WellSiteAutoPilot.Domain.Logic.InstalledLogicModule module) => new(
    module.Id,
    module.ModuleId,
    module.Version,
    module.DisplayName,
    module.Publisher,
    module.Runtime.ToString(),
    module.ExecutionProfile.ToString(),
    module.ManifestJson,
    module.PackageSha256,
    module.TrustStatus.ToString(),
    module.IsEnabled,
    module.InstalledAtUtc);

static ExecutionResponse ToResponse(ExecutionRecord execution) => new(
    execution.Id,
    execution.LogicInstanceId,
    execution.ModuleId,
    execution.ModuleVersion,
    execution.ConfigurationRevisionId,
    execution.AssetId,
    execution.AssetExternalId,
    execution.Quantity,
    execution.RequestContractVersion,
    execution.Trigger.ToString(),
    execution.ScheduledForUtc,
    execution.Mode.ToString(),
    execution.Status.ToString(),
    execution.CorrelationId,
    execution.RequestedAtUtc,
    execution.StartedAtUtc,
    execution.CompletedAtUtc,
    execution.ResultCode,
    execution.FailureCode,
    execution.OutputJson);

static SecurityUserResponse ToSecurityUserResponse(
    WellSiteAutoPilot.Domain.Security.UserAccessProfile user) => new(
    user.Id,
    user.IdentityName,
    user.DisplayName,
    user.IsActive,
    user.CreatedAtUtc,
    user.LastSeenAtUtc,
    user.Roles.Select(item => item.ToString()).ToArray(),
    user.AssetScopeIds.ToArray());

static Guid GetCurrentUserId(System.Security.Claims.ClaimsPrincipal user)
{
    var value = user.FindFirst(SecurityClaimTypes.UserId)?.Value;

    if (!Guid.TryParse(value, out var userId))
    {
        throw new WellSiteAutoPilotException(
            "SECURITY_USER_CONTEXT_REQUIRED",
            FailureKind.Unauthorized,
            "The authenticated WellSite AutoPilot user context is unavailable.");
    }

    return userId;
}

static IReadOnlyCollection<ApplicationRole> ParseApplicationRoles(
    IReadOnlyCollection<string>? roleNames)
{
    if (roleNames is null)
    {
        throw new WellSiteAutoPilotException(
            FailureCodes.ValidationFailed,
            FailureKind.Validation,
            "Roles are required.");
    }

    var roles = new List<ApplicationRole>();

    foreach (var roleName in roleNames)
    {
        if (!Enum.TryParse<ApplicationRole>(
                roleName,
                ignoreCase: true,
                out var role))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed,
                FailureKind.Validation,
                $"Unknown application role '{roleName}'.");
        }

        roles.Add(role);
    }

    return roles.Distinct().OrderBy(item => item).ToArray();
}

public partial class Program;
