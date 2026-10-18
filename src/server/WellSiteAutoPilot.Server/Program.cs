using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using WellSiteAutoPilot.Api.Contracts.Assets;
using WellSiteAutoPilot.Api.Contracts.Executions;
using WellSiteAutoPilot.Api.Contracts.Logic;
using WellSiteAutoPilot.Api.Contracts.ConfiguredLogic;
using WellSiteAutoPilot.Api.Contracts.Recommendations;
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
using WellSiteAutoPilot.Domain.Recommendations;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Http;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Infrastructure.Messaging;
using WellSiteAutoPilot.Infrastructure.System;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Server;
using WellSiteAutoPilot.Server.Scheduling;
using WellSiteAutoPilot.Server.Security;
using WellSiteAutoPilot.Server.Contracts;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = FrontendStaticRoot.Resolve(
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
        Environment.GetEnvironmentVariable("Frontend__DevelopmentStaticRoot"),
        Path.Combine(AppContext.BaseDirectory, "wwwroot"))
});

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Weatherford.WellSiteAutoPilot.Server";
});
builder.Services.AddHealthChecks();
var securityOptions = SecurityOptionsLoader.Load(
    builder.Configuration);
builder.Services.Configure<SecurityOptions>(
    options =>
    {
        options.AuthenticationMode = securityOptions.AuthenticationMode;
        options.BootstrapAdministrators =
            securityOptions.BootstrapAdministrators;
    });

var authenticationMode = securityOptions.AuthenticationMode;
var windowsAuthenticationDiagnostics = builder.Configuration.GetValue("Diagnostics:WindowsAuthentication", false);
if (windowsAuthenticationDiagnostics && (!builder.Environment.IsEnvironment("CI") ||
    !string.Equals(authenticationMode, SecurityOptions.NegotiateAuthenticationMode, StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException("Windows authentication diagnostics require CI with native Negotiate authentication.");


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

builder.Services.AddSingleton<IAuthenticatedPrincipalResolver>(
    new ConfiguredPrincipalResolver(authenticationMode));
builder.Services.AddMemoryCache();
if (string.Equals(authenticationMode, SecurityOptions.NegotiateAuthenticationMode, StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IDirectoryDisplayNameResolver, WindowsDirectoryDisplayNameResolver>();
else
    builder.Services.AddSingleton<IDirectoryDisplayNameResolver, NoDirectoryDisplayNameResolver>();
builder.Services.AddAuthorization(WellSitePolicies.AddGrantPolicies);
builder.Services.AddSingleton<IIdentityGroupResolver>(services =>
    new WindowsTokenGroupResolver(authenticationMode, services.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, GrantPermissionHandler>();
builder.Services.AddScoped<RequestAccessContext>();

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
builder.Services.AddScoped<ManualShadowRequestService>();
builder.Services.AddScoped<LogicModuleCatalogService>();
builder.Services.AddScoped<LogicModuleApprovalService>();
builder.Services.AddScoped<ConfiguredLogicService>();
builder.Services.AddScoped<ScheduledShadowSchedulerService>();
builder.Services.AddScoped<RecommendationMaterializer>();
builder.Services.AddScoped<RecommendationDecisionService>();
builder.Services.AddScoped<RecommendationQueryService>();
builder.Services.AddScoped<UserIdentityService>();
builder.Services.AddScoped<RoleCatalogService>();
builder.Services.AddScoped<AccessGrantAdministrationService>();
builder.Services.Configure<ScheduledShadowSchedulerOptions>(
    builder.Configuration.GetSection(ScheduledShadowSchedulerOptions.SectionName));
builder.Services.AddScoped<OutboxPublisher>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddSingleton(new JetStreamConsumerSettings(
    builder.Configuration.GetValue("Messaging:JetStream:RequestAckWaitSeconds", 120),
    builder.Configuration.GetValue("Messaging:JetStream:ResultAckWaitSeconds", 30),
    builder.Configuration.GetValue("Messaging:JetStream:RequestProgressSeconds", 20),
    builder.Configuration.GetValue("Messaging:JetStream:MaxDeliver", 5)).Validate());
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
if (windowsAuthenticationDiagnostics) app.UseMiddleware<WindowsAuthenticationDiagnosticsMiddleware>();
app.UseAuthentication();
app.UseMiddleware<GrantIdentityMiddleware>();
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
                    component.Status.ToContract()))
                .ToArray());
    })
    .WithName("GetSystemInformation")
    .Produces<SystemInfoResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status500InternalServerError,
        "application/problem+json");

v1.MapGet(
    "/security/me",
    async (UserIdentityService identities, IRequestAccessRepository grants, RequestAccessContext access,
        CancellationToken cancellationToken) =>
    {
        var user = await identities.GetRequiredAsync(access.Subject.UserId, cancellationToken);
        var permissions = await grants.GetPermissionHintsAsync(access.Subject, cancellationToken);
        return new CurrentUserResponse(user.Id, user.IdentityName, user.DisplayName, user.IsActive, permissions);
    })
    .WithName("GetCurrentUser")
    .Produces<CurrentUserResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status401Unauthorized);

v1.MapGet(
    "/security/users",
    async (int? limit, bool? awaitingAccess, ISecurityDirectoryRepository directory, RequestAccessContext access,
        CancellationToken cancellationToken) =>
        (await directory.ListUsersAuthorizedAsync(access.Subject, limit ?? 100, awaitingAccess ?? false, cancellationToken))
            .Select(ToSecurityUserResponse).ToArray())
    .WithName("ListSecurityUsers")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<SecurityUserResponse[]>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status403Forbidden);

v1.MapGet(
    "/security/role-permissions",
    () => ProductPermissionCatalog.Definitions
        .Select(item => new RolePermissionDefinitionResponse(
            item.Key, item.Category, item.ScopeKind, item.IsPrivileged))
        .ToArray())
    .WithName("ListProductPermissions")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<RolePermissionDefinitionResponse[]>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status403Forbidden);

v1.MapGet(
    "/security/roles",
    async (int? limit, ISecurityDirectoryRepository directory, RequestAccessContext access, CancellationToken cancellationToken) =>
        (await directory.ListRolesAuthorizedAsync(access.Subject, limit ?? 100, cancellationToken)).Select(ToRoleResponse).ToArray())
    .WithName("ListSecurityRoles")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<RoleDefinitionResponse[]>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status403Forbidden);

v1.MapGet(
    "/security/roles/{roleId:guid}",
    async (Guid roleId, ISecurityDirectoryRepository directory, RequestAccessContext access, CancellationToken cancellationToken) =>
        ToRoleResponse(RequireFound(await directory.GetRoleAuthorizedAsync(access.Subject, roleId, cancellationToken), "SECURITY_ROLE_NOT_FOUND")))
    .WithName("GetSecurityRole")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<RoleDefinitionResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

v1.MapPost(
    "/security/roles",
    async (CreateSecurityRoleRequest request, RoleCatalogService catalog,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
    {
        var role = await catalog.CreateAuthorizedAsync(
            new RoleMutationCommand(request.Name, request.Description, request.Permissions, true),
            access.Subject, access.CreateActor(context), cancellationToken);
        return Results.Created($"/api/v1/security/roles/{role.Id}", ToRoleResponse(role));
    })
    .WithName("CreateSecurityRole")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<RoleDefinitionResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

v1.MapPut(
    "/security/roles/{roleId:guid}",
    async (Guid roleId, UpdateSecurityRoleRequest request, RoleCatalogService catalog,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
    {
        var role = await catalog.UpdateAuthorizedAsync(roleId, request.ExpectedRevision,
            new RoleMutationCommand(request.Name, request.Description, request.Permissions, request.IsActive),
            access.Subject, access.CreateActor(context), cancellationToken);
        return Results.Ok(ToRoleResponse(role));
    })
    .WithName("UpdateSecurityRole")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<RoleDefinitionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

v1.MapGet(
    "/security/users/{userId:guid}/grants",
    async (Guid userId, ISecurityDirectoryRepository directory, RequestAccessContext access, CancellationToken cancellationToken) =>
        (await directory.ListGrantsAuthorizedAsync(access.Subject, AccessGrantSubject.ForUser(userId), cancellationToken)).Select(ToAccessGrantResponse).ToArray())
    .WithName("ListUserAccessGrants")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<AccessGrantResponse[]>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status403Forbidden);

v1.MapPost(
    "/security/users/{userId:guid}/grants",
    async (Guid userId, CreateAccessGrantRequest request, AccessGrantAdministrationService grants,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
    {
        var scope = request.ScopeKind.ToDomain();
        var grant = await grants.CreateAuthorizedAsync(AccessGrantSubject.ForUser(userId),
            new CreateAccessGrantCommand(request.RoleId, scope, request.AssetIds ?? [], request.ExpectedRoleRevision),
            access.Subject, access.CreateActor(context), cancellationToken);
        return Results.Created(context.Request.Path.ToString(), ToAccessGrantResponse(grant));
    })
    .WithName("CreateUserAccessGrant")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<AccessGrantResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status409Conflict, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

v1.MapPut(
    "/security/users/{userId:guid}/grants/{grantId:guid}/deactivate",
    async (Guid userId, Guid grantId, DeactivateAccessGrantRequest request, AccessGrantAdministrationService grants,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
        ToAccessGrantResponse(await grants.DeactivateAuthorizedAsync(AccessGrantSubject.ForUser(userId), grantId, request.ExpectedRevision,
            access.Subject, access.CreateActor(context), cancellationToken)))
    .WithName("DeactivateUserAccessGrant")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<AccessGrantResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

v1.MapGet(
    "/security/windows-groups/{groupSid}/grants",
    async (string groupSid, ISecurityDirectoryRepository directory, RequestAccessContext access, CancellationToken cancellationToken) =>
        (await directory.ListGrantsAuthorizedAsync(access.Subject, AccessGrantSubject.ForWindowsGroup(groupSid), cancellationToken)).Select(ToAccessGrantResponse).ToArray())
    .WithName("ListWindowsGroupAccessGrants")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<AccessGrantResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

v1.MapPost(
    "/security/windows-groups/{groupSid}/grants",
    async (string groupSid, CreateAccessGrantRequest request, AccessGrantAdministrationService grants,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
    {
        var scope = request.ScopeKind.ToDomain();
        var grant = await grants.CreateAuthorizedAsync(AccessGrantSubject.ForWindowsGroup(groupSid),
            new CreateAccessGrantCommand(request.RoleId, scope, request.AssetIds ?? [], request.ExpectedRoleRevision),
            access.Subject, access.CreateActor(context), cancellationToken);
        return Results.Created(context.Request.Path.ToString(), ToAccessGrantResponse(grant));
    })
    .WithName("CreateWindowsGroupAccessGrant")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<AccessGrantResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status409Conflict, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

v1.MapPut(
    "/security/windows-groups/{groupSid}/grants/{grantId:guid}/deactivate",
    async (string groupSid, Guid grantId, DeactivateAccessGrantRequest request, AccessGrantAdministrationService grants,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
        ToAccessGrantResponse(await grants.DeactivateAuthorizedAsync(AccessGrantSubject.ForWindowsGroup(groupSid), grantId, request.ExpectedRevision,
            access.Subject, access.CreateActor(context), cancellationToken)))
    .WithName("DeactivateWindowsGroupAccessGrant")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<AccessGrantResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

v1.MapPost(
    "/asset-types",
    async (CreateAssetTypeRequest request, AssetService service, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
    {
        var item = await service.CreateAssetTypeAuthorizedAsync(new CreateAssetTypeCommand(request.Key,
            request.DisplayName, request.AttributeSchemaJson), access.Subject, access.CreateActor(context), cancellationToken);
        return Results.Created($"/api/v1/asset-types/{item.Id}", ToAssetTypeResponse(item));
    })
    .WithName("CreateAssetType")
    .RequireAuthorization(WellSitePolicies.AssetsManage)
    .Produces<AssetTypeResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapGet(
    "/asset-types",
    async (IAssetRepository assets, RequestAccessContext access, CancellationToken cancellationToken) =>
        (await assets.ListAssetTypesAuthorizedAsync(access.Subject, cancellationToken))
            .Select(ToAssetTypeResponse)
            .ToArray())
    .WithName("ListAssetTypes")
    .RequireAuthorization(WellSitePolicies.AssetsRead)
    .Produces<AssetTypeResponse[]>(StatusCodes.Status200OK);

v1.MapGet(
    "/asset-types/{assetTypeId:guid}",
    async (Guid assetTypeId, IAssetRepository assets, RequestAccessContext access, CancellationToken cancellationToken) =>
        ToAssetTypeResponse(RequireFound(await assets.GetAssetTypeAuthorizedAsync(access.Subject, assetTypeId, cancellationToken), "ASSET_TYPE_NOT_FOUND")))
    .WithName("GetAssetType")
    .RequireAuthorization(WellSitePolicies.AssetsRead)
    .Produces<AssetTypeResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapGet(
    "/assets/capabilities",
    async (IRequestAccessRepository grants, RequestAccessContext access, CancellationToken cancellationToken) =>
    {
        var hints = await grants.GetPermissionHintsAsync(access.Subject, cancellationToken);
        var root = await grants.HasAdministratorAsync(access.Subject, cancellationToken) &&
            await grants.HasAllAssetPermissionAsync(access.Subject, WellSitePermissions.AssetsManage, cancellationToken);
        return new AssetCreationCapabilitiesResponse(root, hints.Contains(WellSitePermissions.AssetsManage));
    })
    .WithName("GetAssetCreationCapabilities")
    .RequireAuthorization(WellSitePolicies.AssetsRead)
    .Produces<AssetCreationCapabilitiesResponse>(StatusCodes.Status200OK);

v1.MapPost(
    "/assets",
    async (CreateAssetRequest request, AssetService service, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
    {
        var item = await service.CreateAssetAuthorizedAsync(new CreateAssetCommand(request.AssetTypeId, request.Name,
            request.ParentAssetId, request.AttributeValuesJson), access.Subject, access.CreateActor(context), cancellationToken);
        return Results.Created($"/api/v1/assets/{item.Id}", ToAssetResponse(item));
    })
    .WithName("CreateAsset")
    .RequireAuthorization(WellSitePolicies.AssetsManage)
    .Produces<AssetResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapGet(
    "/security/assets/directory",
    async (int? offset, int? limit, ISecurityDirectoryRepository directory, RequestAccessContext access, CancellationToken cancellationToken) =>
        (await directory.ListAssetsAuthorizedAsync(access.Subject, offset ?? 0, limit ?? 200, cancellationToken))
            .Select(asset => new AssetDirectoryEntryResponse(asset.Id, asset.Name, asset.IsActive)).ToArray())
    .WithName("ListSecurityAssetDirectory")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<AssetDirectoryEntryResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);


v1.MapGet(
    "/assets",
    async (Guid? assetTypeId, Guid? parentAssetId, int? limit, IAssetRepository assets,
        RequestAccessContext access, CancellationToken cancellationToken) =>
        (await assets.ListAuthorizedAsync(access.Subject, assetTypeId, parentAssetId, PageLimit(limit ?? 100), cancellationToken))
            .Select(ToAssetResponse).ToArray())
    .WithName("ListAssets")
    .RequireAuthorization(WellSitePolicies.AssetsRead)
    .Produces<AssetResponse[]>(StatusCodes.Status200OK);

v1.MapGet(
    "/assets/{assetId:guid}",
    async (Guid assetId, IAssetRepository assets, RequestAccessContext access, CancellationToken cancellationToken) =>
        ToAssetResponse(RequireFound(await assets.GetAuthorizedAsync(access.Subject, assetId, cancellationToken), "ASSET_NOT_FOUND")))
    .WithName("GetAsset")
    .RequireAuthorization(WellSitePolicies.AssetsRead)
    .Produces<AssetResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapPost(
    "/configured-logic",
    async (
        CreateConfiguredLogicRequest request,
        ConfiguredLogicService service,
        RequestAccessContext access,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        if (request.AssetBindings is null || request.DataBindings is null ||
            request.AssetBindings.Any(binding => binding is null) ||
            request.DataBindings.Any(binding => binding is null))
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "Asset and data bindings are required.");
        var created = await service.CreateAuthorizedAsync(
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
            access.Subject, access.CreateActor(httpContext), cancellationToken);

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
    async (int? limit, IConfiguredLogicRepository configurations, IResourceActionRepository actions,
        RequestAccessContext access, CancellationToken cancellationToken) =>
    {
        var items = await configurations.ListAuthorizedAsync(access.Subject, PageLimit(limit ?? 100), cancellationToken);
        var capabilities = (await actions.GetConfiguredLogicActionsAsync(access.Subject, items.Select(item => item.Id).ToArray(), cancellationToken))
            .ToDictionary(item => item.Id);
        return items.Select(item => WithConfigurationActions(item, capabilities.GetValueOrDefault(item.Id))).ToArray();
    })
    .WithName("ListConfiguredLogic")
    .RequireAuthorization(WellSitePolicies.ConfiguredLogicRead)
    .Produces<ConfiguredLogicResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

v1.MapGet(
    "/configured-logic/{configuredLogicId:guid}",
    async (Guid configuredLogicId, IConfiguredLogicRepository configurations, IResourceActionRepository actions,
        RequestAccessContext access, CancellationToken cancellationToken) =>
    {
        var item = RequireFound(await configurations.GetAuthorizedAsync(access.Subject, configuredLogicId, cancellationToken), "CONFIGURED_LOGIC_NOT_FOUND");
        var capability = (await actions.GetConfiguredLogicActionsAsync(access.Subject, [item.Id], cancellationToken)).SingleOrDefault();
        return WithConfigurationActions(item, capability);
    })
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
        RequestAccessContext access,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        if (request.AssetBindings is null || request.DataBindings is null ||
            request.AssetBindings.Any(binding => binding is null) ||
            request.DataBindings.Any(binding => binding is null))
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "Asset and data bindings are required.");
        var revision = await service.CreateRevisionAuthorizedAsync(
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
            access.Subject, access.CreateActor(httpContext), cancellationToken);

        return Results.Created(
            $"/api/v1/configured-logic/{configuredLogicId}/revisions/{revision.Id}",
            ToConfiguredLogicRevisionResponse(revision));
    })
    .WithName("CreateConfiguredLogicRevision")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .RequireAuthorization(WellSitePolicies.ConfiguredLogicManage)
    .Produces<ConfiguredLogicRevisionResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapPost(
    "/configured-logic/{configuredLogicId:guid}/revisions/{revisionId:guid}/validate",
    async (Guid configuredLogicId, Guid revisionId, ConfiguredLogicService service,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
        ToConfiguredLogicRevisionResponse(await service.ValidateRevisionAuthorizedAsync(configuredLogicId, revisionId,
            access.Subject, access.CreateActor(context), cancellationToken)))
    .WithName("ValidateConfiguredLogicRevision")
    .RequireAuthorization(WellSitePolicies.ConfiguredLogicManage)
    .Produces<ConfiguredLogicRevisionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapPost(
    "/configured-logic/{configuredLogicId:guid}/revisions/{revisionId:guid}/activate",
    async (Guid configuredLogicId, Guid revisionId, [Microsoft.AspNetCore.Mvc.FromHeader(Name = "If-Match")] string? ifMatch,
        ConfiguredLogicService service,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
        ToConfiguredLogicRevisionResponse(await service.ActivateRevisionAuthorizedAsync(configuredLogicId, revisionId,
            ifMatch?.Trim('"') ?? "", access.Subject, access.CreateActor(context), cancellationToken)))
    .WithName("ActivateConfiguredLogicRevision")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .RequireAuthorization(WellSitePolicies.ConfiguredLogicManage)
    .Produces<ConfiguredLogicRevisionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapPost(
    "/configured-logic/{configuredLogicId:guid}/revisions/{revisionId:guid}/shadow",
    async (Guid configuredLogicId, Guid revisionId, RequestConfiguredShadowRequest request,
        ManualShadowRequestService requests, RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
    {
        var receipt = await requests.RequestAuthorizedAsync(configuredLogicId, revisionId, request.RequestId,
            access.Subject, access.CreateActor(context), cancellationToken);
        return Results.Ok(new ConfiguredShadowReceiptResponse(receipt.ExecutionId, receipt.ConfiguredLogicId,
            receipt.ConfigurationRevisionId, receipt.ContractVersion, receipt.Mode.ToContract(), receipt.Trigger.ToContract()));
    })
    .WithName("RequestConfiguredShadowExecution")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")
    .RequireAuthorization(WellSitePolicies.ExecutionsRequest)
    .Produces<ConfiguredShadowReceiptResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapGet(
    "/executions",
    async (string? status, int? limit, Guid? configuredLogicId, Guid? assetId, IExecutionRepository executions,
        RequestAccessContext access, CancellationToken cancellationToken) =>
        (await executions.ListAuthorizedAsync(access.Subject, ParseFilter<WellSiteAutoPilot.Domain.Executions.ExecutionStatus>(status), PageLimit(limit ?? 50),
            configuredLogicId, assetId, cancellationToken))
            .Select(ToResponse).ToArray())
    .WithName("ListExecutions")
    .RequireAuthorization(WellSitePolicies.ExecutionsRead)
    .Produces<ExecutionResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest,
        "application/problem+json");

v1.MapGet(
    "/executions/{executionId:guid}",
    async (Guid executionId, IExecutionRepository executions, RequestAccessContext access, CancellationToken cancellationToken) =>
        ToResponse(RequireFound(await executions.GetAuthorizedAsync(access.Subject, executionId, cancellationToken), "EXECUTION_NOT_FOUND")))
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
        RequestAccessContext access, HttpContext context,
        CancellationToken cancellationToken) =>
    {
        var module = await catalog.RegisterAuthorizedAsync(
            new RegisterLogicModuleCommand(
                request.ManifestJson,
                request.PackageSha256,
                WellSiteAutoPilot.Domain.Logic.LogicModuleTrustStatus.Untrusted),
            access.Subject, access.CreateActor(context), cancellationToken);

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

v1.MapPost(
    "/logic-modules/{moduleId}/{moduleVersion}/approve",
    async (
        string moduleId,
        string moduleVersion,
        ChangeLogicModuleApprovalRequest request,
        LogicModuleApprovalService approvals,
        RequestAccessContext access, HttpContext context,
        CancellationToken cancellationToken) =>
        ToLogicModuleResponse(await approvals.ApproveAsync(
            moduleId, moduleVersion, request.Reason, access.Subject, access.CreateActor(context), cancellationToken)))
    .WithName("ApproveLogicModule")
    .RequireAuthorization(WellSitePolicies.LogicManage)
    .Produces<LogicModuleResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapPost(
    "/logic-modules/{moduleId}/{moduleVersion}/retire",
    async (
        string moduleId,
        string moduleVersion,
        ChangeLogicModuleApprovalRequest request,
        LogicModuleApprovalService approvals,
        RequestAccessContext access, HttpContext context,
        CancellationToken cancellationToken) =>
        ToLogicModuleResponse(await approvals.RetireAsync(
            moduleId, moduleVersion, request.Reason, access.Subject, access.CreateActor(context), cancellationToken)))
    .WithName("RetireLogicModule")
    .RequireAuthorization(WellSitePolicies.LogicManage)
    .Produces<LogicModuleResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapGet(
    "/recommendations",
    async (string? status, int? limit, Guid? configuredLogicId, Guid? assetId, IRecommendationRepository recommendations,
        IResourceActionRepository actions, RequestAccessContext access, CancellationToken cancellationToken) =>
    {
        var items = await recommendations.ListAuthorizedAsync(access.Subject, ParseFilter<WellSiteAutoPilot.Domain.Recommendations.RecommendationStatus>(status),
            PageLimit(limit ?? 50, 100), configuredLogicId, assetId, cancellationToken);
        var capabilities = (await actions.GetRecommendationActionsAsync(access.Subject, items.Select(item => item.Id).ToArray(), cancellationToken))
            .ToDictionary(item => item.Id);
        return items.Select(item => ToRecommendationSummaryResponse(item) with { CanDecide = capabilities.GetValueOrDefault(item.Id)?.CanDecide == true }).ToArray();
    })
    .WithName("ListRecommendations")
    .RequireAuthorization(WellSitePolicies.RecommendationsRead)
    .Produces<RecommendationSummaryResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest, "application/problem+json");

v1.MapGet(
    "/recommendations/{recommendationId:guid}",
    async (Guid recommendationId, IRecommendationRepository recommendations, IResourceActionRepository actions,
        RequestAccessContext access, CancellationToken cancellationToken) =>
    {
        var item = RequireFound(await recommendations.GetAuthorizedAsync(access.Subject, recommendationId, cancellationToken), "RECOMMENDATION_NOT_FOUND");
        var capability = (await actions.GetRecommendationActionsAsync(access.Subject, [item.Id], cancellationToken)).SingleOrDefault();
        return ToRecommendationSummaryResponse(item) with { CanDecide = capability?.CanDecide == true };
    })
    .WithName("GetRecommendation")
    .RequireAuthorization(WellSitePolicies.RecommendationsRead)
    .Produces<RecommendationSummaryResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status404NotFound, "application/problem+json");

v1.MapPost(
    "/recommendations/{recommendationId:guid}/decision",
    async (Guid recommendationId, RecommendationDecisionRequest request, IRecommendationRepository recommendations,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
    {
        var decision = request.Decision.ToDomain();
        var item = await recommendations.DecideAuthorizedAsync(access.Subject, recommendationId, decision,
            access.CreateActor(context), request.Reason, cancellationToken);
        return new RecommendationDecisionResponse(item.Id, item.Status.ToContract(), item.DecisionBy, item.DecisionAtUtc, item.DecisionReason);
    })
    .WithName("DecideRecommendation")
    .RequireAuthorization(WellSitePolicies.RecommendationsDecide)
    .Produces<RecommendationDecisionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

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
    revision.Mode.ToContract(),
    revision.ParametersJson,
    revision.Status.ToContract(),
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
    module.Runtime.ToContract(),
    module.ExecutionProfile.ToContract(),
    module.ManifestJson,
    module.PackageSha256,
    module.TrustStatus.ToContract(),
    module.IsEnabled,
    module.InstalledAtUtc);

static RecommendationSummaryResponse ToRecommendationSummaryResponse(
    RecommendationRecord recommendation) => new(
    recommendation.Id,
    recommendation.ExecutionId,
    recommendation.IntentIndex,
    recommendation.ConfiguredLogicId,
    recommendation.ConfigurationRevisionId,
    recommendation.ModuleId,
    recommendation.ModuleVersion,
    recommendation.AssetId,
    recommendation.Code,
    recommendation.Command,
    recommendation.Quantity,
    recommendation.SuggestedValue,
    recommendation.Unit,
    recommendation.ReasonCode,
    recommendation.Status.ToContract(),
    recommendation.CreatedAtUtc,
    recommendation.ExpiresAtUtc,
    recommendation.DecisionBy,
    recommendation.DecisionAtUtc,
    recommendation.DecisionReason,
    recommendation.ControlActionId);

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
    execution.Trigger.ToContract(),
    execution.ScheduledForUtc,
    execution.Mode.ToContract(),
    execution.Status.ToContract(),
    execution.CorrelationId,
    execution.RequestedAtUtc,
    execution.StartedAtUtc,
    execution.CompletedAtUtc,
    execution.ResultCode,
    execution.FailureCode,
    execution.OutputJson);

static RoleDefinitionResponse ToRoleResponse(
    WellSiteAutoPilot.Domain.Security.RoleDefinition role) => new(
    role.Id,
    role.Key,
    role.Name,
    role.Description,
    role.IsSystem,
    role.IsActive,
    role.Revision,
    role.CreatedAtUtc,
    role.UpdatedAtUtc,
    role.Permissions.ToArray());

static SecurityUserResponse ToSecurityUserResponse(
    WellSiteAutoPilot.Application.Security.SecurityUserDirectoryEntry entry) => new(
    entry.User.Id,
    entry.User.IdentityName,
    entry.User.DisplayName,
    entry.User.IsActive,
    entry.User.CreatedAtUtc,
    entry.User.LastSeenAtUtc,
    entry.ActiveDirectGrantCount);

static T RequireFound<T>(T? value, string code) where T : class => value ??
    throw new WellSiteAutoPilotException(code, FailureKind.NotFound, "The requested resource was not found.");

static int PageLimit(int limit, int maximum = 500) => limit >= 1 && limit <= maximum ? limit :
    throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation, "The requested page limit is invalid.");

static T? ParseFilter<T>(string? raw) where T : struct, Enum => raw is null ? null :
    Enum.TryParse<T>(raw, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value :
        throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation, "The requested status filter is invalid.");

static ConfiguredLogicResponse WithConfigurationActions(ConfiguredLogicDefinition item, ConfiguredLogicActions? capability) =>
    ToConfiguredLogicResponse(item) with { CanManage = capability?.CanManage == true, CanRequestShadow = capability?.CanRequestShadow == true };

static AccessGrantResponse ToAccessGrantResponse(AccessGrant grant) =>
    new(grant.Id, grant.SubjectKind.ToContract(), grant.UserId,
        grant.GroupSid, grant.RoleId, grant.ScopeKind.ToContract(),
        grant.AssetIds.ToArray(), grant.IsActive, grant.Revision);

public partial class Program;
