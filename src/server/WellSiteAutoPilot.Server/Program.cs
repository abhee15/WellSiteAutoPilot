using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using WellSiteAutoPilot.Api.Contracts.Assets;
using WellSiteAutoPilot.Api.Contracts.Connections;
using WellSiteAutoPilot.Api.Contracts.Executions;
using WellSiteAutoPilot.Api.Contracts.Logic;
using WellSiteAutoPilot.Api.Contracts.ConfiguredLogic;
using WellSiteAutoPilot.Api.Contracts.Recommendations;
using WellSiteAutoPilot.Api.Contracts.System;
using WellSiteAutoPilot.Api.Contracts.Security;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Assets.Views;
using WellSiteAutoPilot.Application.Audit;
using WellSiteAutoPilot.Application.Changes;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Connections.Discovery;
using WellSiteAutoPilot.Application.Connections.Importing;
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
using WellSiteAutoPilot.Http.ServiceCallers;
using WellSiteAutoPilot.Server.Assets;
using WellSiteAutoPilot.Server.Connections;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Infrastructure.Messaging;
using Microsoft.Extensions.Hosting.WindowsServices;
using WellSiteAutoPilot.Infrastructure.System;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Server;
using WellSiteAutoPilot.Server.Changes;
using WellSiteAutoPilot.Server.Scheduling;
using WellSiteAutoPilot.Server.Security;
using WellSiteAutoPilot.Server.Contracts;
using WellSiteAutoPilot.Telemetry;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = FrontendStaticRoot.Resolve(
        Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
        Environment.GetEnvironmentVariable("Frontend__DevelopmentStaticRoot"),
        Path.Combine(AppContext.BaseDirectory, "wwwroot"))
});

// The installer writes the Server's key for the Integration Gateway here; development and CI supply it as configuration.
builder.Configuration.AddServiceCallerSettingsFile("server.keys.json");

// The installer serves HTTPS with a certificate from the machine store; development has none configured.
var tlsThumbprint = TlsCertificateLoader.ResolveThumbprint(
    builder.Configuration,
    SecurityOptionsLoader.GetProgramDataSettingsPath());
if (tlsThumbprint is not null)
{
    var tlsCertificate = TlsCertificateLoader.Load(tlsThumbprint);
    builder.WebHost.ConfigureKestrel(kestrel =>
        kestrel.ConfigureHttpsDefaults(https => https.ServerCertificate = tlsCertificate));
}

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Weatherford.WellSiteAutoPilot.Server";
});
builder.Services.AddHealthChecks();
builder.Services.AddWellSiteTelemetry(builder.Configuration, "wellsite-autopilot-server");
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
var directoryProvider = builder.Configuration["Security:DirectoryProvider"] ??
    (string.Equals(authenticationMode, SecurityOptions.NegotiateAuthenticationMode, StringComparison.OrdinalIgnoreCase) ? "active-directory" : "none");
builder.Services.AddSingleton<IGroupDirectory>(services => directoryProvider switch
{
    "active-directory" => new WindowsGroupDirectory(),
    "none" => new UnavailableGroupDirectory(),
    _ => throw new InvalidOperationException("Unsupported Security:DirectoryProvider.")
});
builder.Services.AddScoped<GroupDirectorySearchService>();
builder.Services.AddAuthorization(WellSitePolicies.AddGrantPolicies);
builder.Services.AddSingleton<IIdentityGroupResolver>(services =>
    new WindowsTokenGroupResolver(authenticationMode, services.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, GrantPermissionHandler>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, GrantAnyPermissionHandler>();
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
    client.MaxResponseContentBufferSize = 8192;
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
builder.Services.AddHttpClient<IGatewayAdapters, GatewayAdapterClient>(client =>
{
    client.BaseAddress = new Uri(
        (builder.Configuration["Runtime:IntegrationGatewayUrl"] ?? "http://127.0.0.1:5081").TrimEnd('/') + "/");
    // A test is limited to 10 seconds and a discovery to 120 by the Gateway; this allows a little over the longer one.
    client.Timeout = TimeSpan.FromSeconds(150);
});
builder.Services.AddSingleton<DiscoverySnapshotStore>();
builder.Services.AddScoped<ConnectionService>();
builder.Services.AddScoped<ConnectionMonitorService>();
builder.Services.Configure<ConnectionMonitorOptions>(
    builder.Configuration.GetSection(ConnectionMonitorOptions.SectionName));
builder.Services.AddScoped<ConnectionImportService>();
builder.Services.AddScoped<SourceChangeService>();
builder.Services.AddScoped<ExecutionService>();
builder.Services.AddScoped<ManualShadowRequestService>();
builder.Services.AddScoped<LogicModuleCatalogService>();
builder.Services.AddScoped<LogicModulePackageService>();
builder.Services.AddScoped<LogicModuleApprovalService>();
builder.Services.AddScoped<ConfiguredLogicService>();
builder.Services.AddSingleton(new ScheduledReadAuthorityOptions
{
    ServicePrincipalUserId = builder.Configuration.GetValue<Guid?>("Scheduler:ReadAuthority:ServicePrincipalUserId")
});
builder.Services.AddScoped<IDelegatedAuthorityAuditWriter, WellSiteAutoPilot.Persistence.Scheduling.DelegatedAuthorityAuditWriter>();
builder.Services.AddScoped<IScheduledReadAuthorityProvider, ScheduledReadAuthorityProvider>();
builder.Services.AddScoped<ScheduledShadowSchedulerService>();
builder.Services.AddScoped<RecommendationMaterializer>();
builder.Services.AddScoped<RecommendationDecisionService>();
builder.Services.AddScoped<RecommendationQueryService>();
builder.Services.AddSingleton(new WellSiteAutoPilot.Application.Control.AutonomousControlOptions
{
    Enabled = builder.Configuration.GetValue<bool>("Control:Autonomous:Enabled")
});
builder.Services.AddScoped<WellSiteAutoPilot.Application.Control.IControlRepository, WellSiteAutoPilot.Persistence.Control.ControlRepository>();
builder.Services.AddSingleton<WellSiteAutoPilot.Application.Control.SimulatedControlCommandSink>();
builder.Services.AddSingleton<WellSiteAutoPilot.Application.Control.IControlCommandSink>(
    sp => sp.GetRequiredService<WellSiteAutoPilot.Application.Control.SimulatedControlCommandSink>());
builder.Services.AddSingleton<WellSiteAutoPilot.Application.Control.IControlCommandSink, WellSiteAutoPilot.Application.Control.RefusingProviderControlCommandSink>();
builder.Services.AddScoped<WellSiteAutoPilot.Application.Control.AutonomousControlService>();
builder.Services.AddScoped<UserIdentityService>();
builder.Services.AddScoped<RoleCatalogService>();
builder.Services.AddScoped<AccessGrantAdministrationService>();
builder.Services.Configure<ScheduledShadowSchedulerOptions>(
    builder.Configuration.GetSection(ScheduledShadowSchedulerOptions.SectionName));
builder.Services.AddScoped<OutboxPublisher>();
builder.Services.AddScoped<IModulePackageObjectStore, NatsModulePackageObjectStore>();
builder.Services.AddScoped<ModulePackagePublisher>();
builder.Services.AddHostedService<ModulePackagePublicationDispatcher>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddSingleton(new JetStreamConsumerSettings(
    builder.Configuration.GetValue("Messaging:JetStream:RequestAckWaitSeconds", 120),
    builder.Configuration.GetValue("Messaging:JetStream:ResultAckWaitSeconds", 30),
    builder.Configuration.GetValue("Messaging:JetStream:RequestProgressSeconds", 20),
    builder.Configuration.GetValue("Messaging:JetStream:MaxDeliver", 5)).Validate());
builder.Services.AddHostedService<ExecutionResultConsumer>();
builder.Services.AddHostedService<ExecutionResultV2Consumer>();
builder.Services.AddScoped<IExecutionMeasurementGateway>(services =>
    (GatewayAdapterClient)services.GetRequiredService<IGatewayAdapters>());
builder.Services.AddHostedService<ExecutionMeasurementReadConsumer>();
builder.Services.AddHostedService<ScheduledShadowSchedulerHostedService>();
builder.Services.AddHostedService<ConnectionMonitorHostedService>();
builder.Services.AddWellSitePersistence(DatabaseConnectionLoader.Resolve(
    builder.Configuration,
    name => Environment.GetEnvironmentVariable(name),
    SecurityOptionsLoader.GetProgramDataSettingsPath(),
    WindowsServiceHelpers.IsWindowsService()));
builder.Services.AddWellSiteMessaging(
    builder.Configuration["Messaging:Nats:Url"] ?? "nats://127.0.0.1:4222",
    builder.Configuration["Messaging:Nats:Token"]);
builder.Services.AddChangeSignal(builder.Configuration);

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
    .RequireAuthorization(WellSitePolicies.SystemStatusRead)
    .Produces<SystemInfoResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status403Forbidden)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status500InternalServerError,
        "application/problem+json");

v1.MapGet(
    "/system/health",
    async (IPlatformInformationService service, CancellationToken cancellationToken) =>
    {
        // The overall state only: no component names, versions or addresses, so any signed-in person may see it.
        var components = await service.GetComponentsAsync(cancellationToken);
        var states = components.Select(component => component.Status.ToContract()).ToArray();
        var unhealthy = states.Count(state => state != PlatformComponentStatus.Healthy);
        var status = states.Contains(PlatformComponentStatus.Unavailable)
            ? PlatformComponentStatus.Unavailable
            : unhealthy > 0 ? PlatformComponentStatus.Degraded : PlatformComponentStatus.Healthy;
        return new SystemHealthResponse(status, components.Count, unhealthy);
    })
    .WithName("GetSystemHealth")
    .Produces<SystemHealthResponse>(StatusCodes.Status200OK)
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
    "/security/audit-events",
    async (int? limit, string? cursor, string? actionPrefix, string? targetType, Guid? actorUserId, Guid? assetId,
        DateTimeOffset? fromUtc, DateTimeOffset? toUtc, ISecurityDirectoryRepository directory,
        RequestAccessContext access, CancellationToken cancellationToken) =>
    {
        AuditEventCursor? after = null;
        if (cursor is not null && !AuditEventCursor.TryDecode(cursor, out after))
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "The audit event cursor is not valid.");
        var page = await directory.ListAuditEventsAuthorizedAsync(
            access.Subject,
            new AuditEventQuery(limit ?? AuditEventQuery.DefaultLimit, after, actionPrefix, targetType,
                actorUserId, assetId, fromUtc, toUtc),
            cancellationToken);
        return new AuditEventPageResponse(
            page.Items.Select(item => new AuditEventResponse(
                item.Id, item.OccurredAtUtc, item.ActorUserId, item.ActorIdentity, item.Action, item.TargetType,
                item.TargetId, item.AssetId, item.CorrelationId, item.DetailsJson, item.TargetName, item.ActorName)).ToArray(),
            page.Next?.Encode());
    })
    .WithName("ListAuditEvents")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<AuditEventPageResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
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
    "/security/groups",
    async (ISecurityDirectoryRepository directory, RequestAccessContext access, CancellationToken cancellationToken) =>
        (await directory.ListAssignedGroupsAuthorizedAsync(access.Subject, cancellationToken)).Select(group =>
            new WellSiteAutoPilot.Api.Contracts.Security.DirectoryGroupResponse(group.DirectoryId, group.ExternalId, group.DisplayName, group.Domain)).ToArray())
    .WithName("ListAssignedGroups")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<WellSiteAutoPilot.Api.Contracts.Security.DirectoryGroupResponse[]>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status403Forbidden);

v1.MapGet(
    "/security/directory",
    async (GroupDirectorySearchService directory, RequestAccessContext access, CancellationToken cancellationToken) =>
    {
        var capability = await directory.GetCapabilitiesAuthorizedAsync(access.Subject, cancellationToken);
        return new WellSiteAutoPilot.Api.Contracts.Security.GroupDirectoryCapabilitiesResponse(capability.DirectoryId, capability.SupportsGroupSearch);
    })
    .WithName("GetGroupDirectoryCapabilities")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<WellSiteAutoPilot.Api.Contracts.Security.GroupDirectoryCapabilitiesResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status403Forbidden);

v1.MapGet(
    "/security/directory/groups",
    async (string prefix, GroupDirectorySearchService directory, RequestAccessContext access, CancellationToken cancellationToken) =>
    {
        var page = await directory.SearchAuthorizedAsync(access.Subject, prefix, cancellationToken);
        return new WellSiteAutoPilot.Api.Contracts.Security.GroupDirectorySearchResponse(
            page.Groups.Select(group => new WellSiteAutoPilot.Api.Contracts.Security.DirectoryGroupResponse(
                group.DirectoryId, group.ExternalId, group.DisplayName, group.Domain)).ToArray(), page.HasMore);
    })
    .WithName("SearchDirectoryGroups")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<WellSiteAutoPilot.Api.Contracts.Security.GroupDirectorySearchResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

v1.MapGet(
    "/security/directories/{directoryId}/group-grants",
    async (string directoryId, string groupId, ISecurityDirectoryRepository directory, RequestAccessContext access, CancellationToken cancellationToken) =>
        (await directory.ListGrantsAuthorizedAsync(access.Subject, AccessGrantSubject.ForGroup(directoryId, groupId), cancellationToken)).Select(ToAccessGrantResponse).ToArray())
    .WithName("ListGroupAccessGrants")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<AccessGrantResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

v1.MapPost(
    "/security/directories/{directoryId}/group-grants",
    async (string directoryId, string groupId, CreateAccessGrantRequest request, AccessGrantAdministrationService grants,
        GroupDirectorySearchService groupDirectory,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
    {
        var scope = request.ScopeKind.ToDomain();
        var subject = await groupDirectory.ResolveAuthorizedAsync(access.Subject, directoryId, groupId, cancellationToken);
        var grant = await grants.CreateAuthorizedAsync(subject,
            new CreateAccessGrantCommand(request.RoleId, scope, request.AssetIds ?? [], request.ExpectedRoleRevision),
            access.Subject, access.CreateActor(context), cancellationToken);
        return Results.Created(context.Request.Path.ToString(), ToAccessGrantResponse(grant));
    })
    .WithName("CreateGroupAccessGrant")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<AccessGrantResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status409Conflict, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

v1.MapPut(
    "/security/directories/{directoryId}/group-grants/{grantId:guid}/deactivate",
    async (string directoryId, string groupId, Guid grantId, DeactivateAccessGrantRequest request, AccessGrantAdministrationService grants,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
        ToAccessGrantResponse(await grants.DeactivateAuthorizedAsync(AccessGrantSubject.ForGroup(directoryId, groupId), grantId, request.ExpectedRevision,
            access.Subject, access.CreateActor(context), cancellationToken)))
    .WithName("DeactivateGroupAccessGrant")
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
    .NotifiesChange(ChangeArea.Assets)
    .RequireAuthorization(WellSitePolicies.AssetTypesManage)
    .Produces<AssetTypeResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapPut(
    "/asset-types/{assetTypeId:guid}/hide",
    async (Guid assetTypeId, HideAssetTypeRequest request, IAssetRepository assets, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
    {
        await assets.SetAssetTypeHiddenAuthorizedAsync(assetTypeId, true, request.Note, access.Subject,
            access.CreateActor(context), cancellationToken);
        return Results.NoContent();
    })
    .WithName("HideAssetType")
    .NotifiesChange(ChangeArea.Assets)
    .RequireAuthorization(WellSitePolicies.AssetTypesHide)
    .Produces(StatusCodes.Status204NoContent)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapPut(
    "/asset-types/{assetTypeId:guid}/unhide",
    async (Guid assetTypeId, IAssetRepository assets, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
    {
        await assets.SetAssetTypeHiddenAuthorizedAsync(assetTypeId, false, null, access.Subject,
            access.CreateActor(context), cancellationToken);
        return Results.NoContent();
    })
    .WithName("UnhideAssetType")
    .NotifiesChange(ChangeArea.Assets)
    .RequireAuthorization(WellSitePolicies.AssetTypesManage)
    .Produces(StatusCodes.Status204NoContent)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapDelete(
    "/asset-types/{assetTypeId:guid}",
    async (Guid assetTypeId, IAssetRepository assets, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
    {
        await assets.DeleteAssetTypeAuthorizedAsync(assetTypeId, access.Subject, access.CreateActor(context), cancellationToken);
        return Results.NoContent();
    })
    .WithName("DeleteAssetType")
    .NotifiesChange(ChangeArea.Assets)
    .RequireAuthorization(WellSitePolicies.AssetTypesManage)
    .Produces(StatusCodes.Status204NoContent)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
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
        var root = await grants.HasAllAssetPermissionAsync(access.Subject, WellSitePermissions.AssetsManage, cancellationToken);
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
    .NotifiesChange(ChangeArea.Assets)
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
    "/assets/children",
    async (Guid? parentAssetId, Guid? ownerConnectionId, bool? standalone, int? offset, int? limit, IAssetRepository assets,
        RequestAccessContext access, CancellationToken cancellationToken) =>
    {
        var owner = AssetBrowseOwnerScope(ownerConnectionId, standalone);
        return (await assets.ListChildrenAuthorizedAsync(access.Subject, parentAssetId, owner, PageOffset(offset),
            PageLimit(limit ?? 100), cancellationToken))
            .Select(node => new AssetTreeNodeResponse(node.Asset.Id, node.Asset.AssetTypeId, node.Asset.Name,
                node.Asset.ParentAssetId, node.Asset.IsActive, node.ChildCount, node.Asset.OwnerConnectionId)).ToArray();
    })
    .WithName("ListAssetChildren")
    .RequireAuthorization(WellSitePolicies.AssetsRead)
    .Produces<AssetTreeNodeResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

v1.MapGet(
    "/assets/owners",
    async (IAssetRepository assets, RequestAccessContext access, CancellationToken cancellationToken) =>
        (await assets.ListOwnersAuthorizedAsync(access.Subject, cancellationToken))
            .Select(owner => new AssetOwnerResponse(owner.ConnectionId, owner.ConnectionName, owner.IsEnabled,
                owner.TopLevelCount, owner.AssetCount)).ToArray())
    .WithName("ListAssetOwners")
    .RequireAuthorization(WellSitePolicies.AssetsRead)
    .Produces<AssetOwnerResponse[]>(StatusCodes.Status200OK);

v1.MapGet("/asset-views",
    async (IAssetViewRepository views, IRequestAccessRepository grants, RequestAccessContext access, CancellationToken cancellationToken) =>
    {
        var held = await GrantPermissionChecker.HeldAsync(grants, access.Subject,
            [WellSitePermissions.AssetViewsEdit, WellSitePermissions.AssetViewsDelete], cancellationToken);
        return (await views.ListAsync(access.Subject, cancellationToken)).Select(view => AssetViewMapping.ToResponse(view, held)).ToArray();
    })
    .WithName("ListAssetViews").RequireAuthorization(WellSitePolicies.ConnectionsRead)
    .Produces<AssetViewResponse[]>(StatusCodes.Status200OK);

v1.MapGet("/asset-views/{viewId:guid}",
    async (Guid viewId, IAssetViewRepository views, IRequestAccessRepository grants, RequestAccessContext access, CancellationToken cancellationToken) =>
    {
        var held = await GrantPermissionChecker.HeldAsync(grants, access.Subject,
            [WellSitePermissions.AssetViewsEdit, WellSitePermissions.AssetViewsDelete], cancellationToken);
        return AssetViewMapping.ToResponse(RequireFound(await views.FindAsync(access.Subject, viewId, cancellationToken), AssetViewFailureCodes.NotFound), held);
    })
    .WithName("GetAssetView").RequireAuthorization(WellSitePolicies.ConnectionsRead)
    .Produces<AssetViewResponse>(StatusCodes.Status200OK);

v1.MapPost("/asset-views",
    async (CreateAssetViewRequest request, IAssetViewRepository views, IRequestAccessRepository grants, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
    {
        var created = await views.CreateAsync(access.Subject, access.CreateActor(context), request.ConnectionId,
            request.Name, request.AssetIds, cancellationToken);
        var held = await GrantPermissionChecker.HeldAsync(grants, access.Subject,
            [WellSitePermissions.AssetViewsEdit, WellSitePermissions.AssetViewsDelete], cancellationToken);
        return Results.Created($"/api/v1/asset-views/{created.Id}", AssetViewMapping.ToResponse(created, held));
    })
    .WithName("CreateAssetView").RequireAuthorization(WellSitePolicies.AssetViewsCreate)
    .Produces<AssetViewResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapPut("/asset-views/{viewId:guid}",
    async (Guid viewId, UpdateAssetViewRequest request, IAssetViewRepository views, IRequestAccessRepository grants,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
    {
        var updated = await views.UpdateAsync(access.Subject, access.CreateActor(context), viewId, request.ExpectedRevision,
            request.Name, request.AssetIds, cancellationToken);
        var held = await GrantPermissionChecker.HeldAsync(grants, access.Subject,
            [WellSitePermissions.AssetViewsEdit, WellSitePermissions.AssetViewsDelete], cancellationToken);
        return AssetViewMapping.ToResponse(updated, held);
    })
    .WithName("UpdateAssetView").RequireAuthorization(WellSitePolicies.AssetViewsEdit)
    .Produces<AssetViewResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapDelete("/asset-views/{viewId:guid}",
    async (Guid viewId, int expectedRevision, IAssetViewRepository views, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
    {
        await views.DeleteAsync(access.Subject, access.CreateActor(context), viewId, expectedRevision, cancellationToken);
        return Results.NoContent();
    })
    .WithName("DeleteAssetView").RequireAuthorization(WellSitePolicies.AssetViewsDelete).Produces(StatusCodes.Status204NoContent);

v1.MapGet("/asset-views/{viewId:guid}/members",
    async (Guid viewId, string? q, int? offset, int? limit, IAssetViewRepository views, RequestAccessContext access, CancellationToken cancellationToken) =>
        (await views.MembersAsync(access.Subject, viewId, q, PageOffset(offset), PageLimit(limit ?? 100), cancellationToken))
            .Select(ToAssetResponse).ToArray())
    .WithName("ListAssetViewMembers").RequireAuthorization(WellSitePolicies.ConnectionsRead)
    .Produces<AssetResponse[]>(StatusCodes.Status200OK);

v1.MapGet("/asset-views/{viewId:guid}/selection",
    async (Guid viewId, IAssetViewRepository views, RequestAccessContext access, CancellationToken cancellationToken) =>
    {
        var selection = await views.SelectionAsync(access.Subject, viewId, cancellationToken);
        return new AssetViewSelectionResponse(selection.Revision, selection.AssetIds,
            selection.UnavailableMembers.Select(member => new AssetViewUnavailableMemberResponse(member.Id, member.Name)).ToArray());
    })
    .WithName("GetAssetViewSelection").RequireAuthorization(WellSitePolicies.ConnectionsRead)
    .Produces<AssetViewSelectionResponse>(StatusCodes.Status200OK);

v1.MapGet(
    "/assets/search",
    async (string? q, Guid? assetTypeId, Guid? ownerConnectionId, bool? standalone, int? offset, int? limit, IAssetRepository assets,
        RequestAccessContext access, CancellationToken cancellationToken) =>
        (await assets.SearchAuthorizedAsync(access.Subject, SearchText(q), assetTypeId,
            AssetBrowseOwnerScope(ownerConnectionId, standalone), PageOffset(offset),
            PageLimit(limit ?? 50), cancellationToken))
            .Select(hit => new AssetSearchResultResponse(hit.Asset.Id, hit.Asset.AssetTypeId, hit.Asset.Name,
                hit.Asset.ParentAssetId, hit.Asset.IsActive,
                hit.Path.Select(step => new AssetPathSegmentResponse(step.Id, step.Name)).ToArray(),
                hit.Asset.OwnerConnectionId)).ToArray())
    .WithName("SearchAssets")
    .RequireAuthorization(WellSitePolicies.AssetsRead)
    .Produces<AssetSearchResultResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

v1.MapGet(
    "/assets/{assetId:guid}",
    async (Guid assetId, IAssetRepository assets, RequestAccessContext access, CancellationToken cancellationToken) =>
        ToAssetResponse(RequireFound(await assets.GetAuthorizedAsync(access.Subject, assetId, cancellationToken), "ASSET_NOT_FOUND")))
    .WithName("GetAsset")
    .RequireAuthorization(WellSitePolicies.AssetsRead)
    .Produces<AssetResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapGet(
    "/assets/{assetId:guid}/source",
    async (Guid assetId, IAssetRepository assets, RequestAccessContext access, CancellationToken cancellationToken) =>
    {
        var source = RequireFound(await assets.GetSourceAuthorizedAsync(access.Subject, assetId, cancellationToken),
            "ASSET_SOURCE_NOT_FOUND");
        return new AssetSourceResponse(source.ConnectionId, source.ConnectionName, source.ResourceKind, source.ExternalId,
            source.ImportedAtUtc);
    })
    .WithName("GetAssetSource")
    .RequireAuthorization(WellSitePolicies.AssetsRead)
    .Produces<AssetSourceResponse>(StatusCodes.Status200OK)
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
                        request.Schedule.TimeZoneId),
                request.View is null ? null : new ConfiguredLogicViewBindingCommand(request.View.ViewId, request.View.ExpectedRevision, request.View.Role),
                request.Mode?.ToDomain() ?? WellSiteAutoPilot.Domain.Executions.ExecutionMode.Shadow),
            access.Subject, access.CreateActor(httpContext), cancellationToken);

        return Results.Created(
            $"/api/v1/configured-logic/{created.Id}",
            ToConfiguredLogicResponse(created));
    })
    .WithName("CreateConfiguredLogic")
    .NotifiesChange(ChangeArea.ConfiguredLogic)
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
                        request.Schedule.TimeZoneId),
                request.View is null ? null : new ConfiguredLogicViewBindingCommand(request.View.ViewId, request.View.ExpectedRevision, request.View.Role),
                request.Mode?.ToDomain() ?? WellSiteAutoPilot.Domain.Executions.ExecutionMode.Shadow),
            access.Subject, access.CreateActor(httpContext), cancellationToken);

        return Results.Created(
            $"/api/v1/configured-logic/{configuredLogicId}/revisions/{revision.Id}",
            ToConfiguredLogicRevisionResponse(revision));
    })
    .WithName("CreateConfiguredLogicRevision")
    .NotifiesChange(ChangeArea.ConfiguredLogic)
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
    .NotifiesChange(ChangeArea.ConfiguredLogic)
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
    .NotifiesChange(ChangeArea.ConfiguredLogic)
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
    .NotifiesChange(ChangeArea.Executions)
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
        ToDetailedResponse(RequireFound(await executions.GetAuthorizedAsync(access.Subject, executionId, cancellationToken), "EXECUTION_NOT_FOUND")))
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
    .NotifiesChange(ChangeArea.Modules)
    .RequireAuthorization(WellSitePolicies.LogicManage)
    .Produces<LogicModuleResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapPost(
    "/logic-modules/packages",
    async Task<IResult> (HttpRequest request, LogicModulePackageService packages, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
    {
        if (!string.Equals(request.ContentType?.Split(';')[0].Trim(), "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "Send the .wsamodule archive as application/octet-stream.");
        if (request.ContentLength > LogicModulePackageService.MaximumUploadBytes)
            throw new WellSiteAutoPilotException("MODULE_PACKAGE_UPLOAD_TOO_LARGE", FailureKind.Validation,
                "Module package uploads must not exceed 16 MiB.");
        var module = await packages.UploadAsync(request.Body, access.Subject, access.CreateActor(context), cancellationToken);
        return Results.Created(
            $"/api/v1/logic-modules/{Uri.EscapeDataString(module.ModuleId)}/{Uri.EscapeDataString(module.Version)}",
            ToLogicModuleResponse(module));
    })
    .WithName("UploadLogicModulePackage")
    .Accepts<IFormFile>("application/octet-stream")
    .NotifiesChange(ChangeArea.Modules)
    .RequireAuthorization(WellSitePolicies.LogicManage)
    .Produces<LogicModuleResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status415UnsupportedMediaType, "application/problem+json");

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

v1.MapGet("/logic-modules/{moduleId}/{moduleVersion}/package-state",
    async (string moduleId, string moduleVersion, LogicModuleCatalogService catalog, IModulePackageStateRepository states,
        IModulePackageObjectStore objects, IPlatformInformationService platform, TimeProvider clock, CancellationToken cancellationToken) =>
    {
        var module = await catalog.GetRequiredAsync(moduleId, moduleVersion, cancellationToken);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(6));
        var stateTask = states.ReadPackageStateAsync(module.Id, module.PackageSha256, budget.Token);
        var objectTask = objects.ContainsAsync(module.PackageSha256, budget.Token);
        var platformTask = platform.GetComponentsAsync(budget.Token);
        await Task.WhenAll(stateTask, objectTask, platformTask);
        var state = await stateTask;
        var components = await platformTask;
        var worker = components.SingleOrDefault(c => c.Name == WellSiteAutoPilot.Domain.System.PlatformComponentNames.DotNetRuntime);
        var workerHealthy = worker?.Status == WellSiteAutoPilot.Domain.System.PlatformComponentStatus.Healthy;
        var gatewayHealthy = components.Any(c => c.Name == WellSiteAutoPilot.Domain.System.PlatformComponentNames.Gateway &&
            c.Status == WellSiteAutoPilot.Domain.System.PlatformComponentStatus.Healthy);
        var compatible = false;
        string? compatibilityFailure = "MODULE_RUNTIME_UNAVAILABLE";
        if (workerHealthy && worker?.ModuleSdkVersion is { } sdk)
        {
            using var manifestStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(module.ManifestJson));
            var manifest = await LogicModuleManifestReader.ReadAsync(manifestStream, budget.Token);
            try
            {
                LogicModulePackageCompatibility.ValidateHost(manifest, WellSiteAutoPilot.Domain.Logic.LogicRuntimeKind.DotNet, sdk);
                compatible = true; compatibilityFailure = null;
            }
            catch (WellSiteAutoPilotException failure) { compatibilityFailure = failure.Code; }
        }
        var available = await objectTask;
        var approved = module.IsEnabled && module.TrustStatus is WellSiteAutoPilot.Domain.Logic.LogicModuleTrustStatus.CustomerApproved
            or WellSiteAutoPilot.Domain.Logic.LogicModuleTrustStatus.TrustedPublisher;
        return Results.Ok(new LogicModulePackageStateResponse(state.IsStored, state.PublicationRecorded, available,
            state.PublicationPending, state.AttemptCount, state.LastFailureCode, state.NextAttemptAtUtc,
            compatible, workerHealthy, gatewayHealthy, approved && state.IsStored && state.PublicationRecorded && available &&
            compatible && workerHealthy && gatewayHealthy, compatibilityFailure, clock.GetUtcNow()));
    })
    .WithName("GetLogicModulePackageState")
    .RequireAuthorization(WellSitePolicies.LogicRead)
    .Produces<LogicModulePackageStateResponse>(StatusCodes.Status200OK)
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
    .NotifiesChange(ChangeArea.Modules)
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
    .NotifiesChange(ChangeArea.Modules)
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
    .NotifiesChange(ChangeArea.Recommendations)
    .RequireAuthorization(WellSitePolicies.RecommendationsDecide)
    .Produces<RecommendationDecisionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapGet(
    "/control/kill-switch",
    async (WellSiteAutoPilot.Application.Control.AutonomousControlService control,
        WellSiteAutoPilot.Application.Control.AutonomousControlOptions options, CancellationToken cancellationToken) =>
        ToKillSwitchResponse(await control.GetKillSwitchAsync(cancellationToken), options))
    .WithName("GetControlKillSwitch")
    .RequireAuthorization(WellSitePolicies.RecommendationsRead)
    .Produces<WellSiteAutoPilot.Api.Contracts.Control.KillSwitchResponse>(StatusCodes.Status200OK);

v1.MapPut(
    "/control/kill-switch",
    async (WellSiteAutoPilot.Api.Contracts.Control.SetKillSwitchRequest request,
        WellSiteAutoPilot.Application.Control.AutonomousControlService control,
        WellSiteAutoPilot.Application.Control.AutonomousControlOptions options,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
        ToKillSwitchResponse(await control.SetKillSwitchAsync(request.Engaged, access.CreateActor(context).IdentityName, request.Reason, cancellationToken), options))
    .WithName("SetControlKillSwitch")
    .NotifiesChange(ChangeArea.Recommendations)
    .RequireAuthorization(WellSitePolicies.ControlManage)
    .Produces<WellSiteAutoPilot.Api.Contracts.Control.KillSwitchResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

v1.MapGet(
    "/control/policies/{configuredLogicId:guid}",
    async (Guid configuredLogicId, WellSiteAutoPilot.Application.Control.AutonomousControlService control,
        RequestAccessContext access, CancellationToken cancellationToken) =>
        ToAutonomyPolicyResponse(RequireFound(await control.GetPolicyAsync(access.Subject, configuredLogicId, cancellationToken), "AUTONOMY_POLICY_NOT_FOUND")))
    .WithName("GetAutonomyPolicy")
    .RequireAuthorization(WellSitePolicies.RecommendationsRead)
    .Produces<WellSiteAutoPilot.Api.Contracts.Control.AutonomyPolicyResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapPut(
    "/control/policies/{configuredLogicId:guid}",
    async (Guid configuredLogicId, WellSiteAutoPilot.Api.Contracts.Control.SetAutonomyPolicyRequest request,
        WellSiteAutoPilot.Application.Control.AutonomousControlService control,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
    {
        if (request.Limits is null || request.Limits.Any(item => item is null))
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation, "Command limits are required.");
        var policy = await control.SetPolicyAsync(configuredLogicId, request.Enabled,
            WellSiteAutoPilot.Domain.Control.ControlSinkKinds.Simulator,
            request.Limits.Select(item => new WellSiteAutoPilot.Domain.Control.AutonomyCommandLimit(item.Command, item.Unit, item.MinimumValue, item.MaximumValue)).ToArray(),
            request.MaxActionsPerWindow, request.WindowSeconds, access.CreateActor(context).IdentityName, cancellationToken);
        return ToAutonomyPolicyResponse(policy);
    })
    .WithName("SetAutonomyPolicy")
    .NotifiesChange(ChangeArea.ConfiguredLogic)
    .RequireAuthorization(WellSitePolicies.ControlManage)
    .Produces<WellSiteAutoPilot.Api.Contracts.Control.AutonomyPolicyResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapGet(
    "/control/actions",
    async (Guid? configuredLogicId, int? limit, WellSiteAutoPilot.Application.Control.AutonomousControlService control,
        RequestAccessContext access, CancellationToken cancellationToken) =>
        (await control.ListActionsAsync(access.Subject, configuredLogicId, PageLimit(limit ?? 50, 200), cancellationToken))
            .Select(item => new WellSiteAutoPilot.Api.Contracts.Control.ControlActionResponse(item.Id, item.RecommendationId,
                item.ExecutionId, item.ConfiguredLogicId, item.AssetId, item.Command, item.Quantity, item.Value, item.Unit,
                item.Status.ToString(), item.GateCode, item.Sink, item.SinkReference, item.CreatedAtUtc, item.CompletedAtUtc))
            .ToArray())
    .WithName("ListControlActions")
    .RequireAuthorization(WellSitePolicies.RecommendationsRead)
    .Produces<WellSiteAutoPilot.Api.Contracts.Control.ControlActionResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

v1.MapGet(
    "/connections",
    async (ConnectionService connections, CancellationToken cancellationToken) =>
        (await connections.ListAsync(cancellationToken)).Select(ConnectionMapping.ToResponse).ToArray())
    .WithName("ListConnections")
    .RequireAuthorization(WellSitePolicies.ConnectionsRead)
    .Produces<ConnectionResponse[]>(StatusCodes.Status200OK);

v1.MapGet(
    "/connections/adapters",
    async (ConnectionService connections, CancellationToken cancellationToken) =>
        (await connections.ListAdaptersAsync(cancellationToken)).Select(ConnectionMapping.ToResponse).ToArray())
    .WithName("ListConnectionAdapters")
    .RequireAuthorization(WellSitePolicies.ConnectionsRead)
    .Produces<AdapterResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

v1.MapGet(
    "/connections/{connectionId:guid}",
    async (Guid connectionId, ConnectionService connections, CancellationToken cancellationToken) =>
        ConnectionMapping.ToResponse(await connections.GetAsync(connectionId, cancellationToken)))
    .WithName("GetConnection")
    .RequireAuthorization(WellSitePolicies.ConnectionsRead)
    .Produces<ConnectionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapPost(
    "/connections",
    async (CreateConnectionRequest request, ConnectionService connections, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
    {
        var item = await connections.CreateAsync(new CreateConnectionCommand(request.Name, request.AdapterKey, request.Settings),
            access.Subject, access.CreateActor(context), cancellationToken);
        return Results.Created($"/api/v1/connections/{item.Id}", ConnectionMapping.ToResponse(item));
    })
    .WithName("CreateConnection")
    .NotifiesChange(ChangeArea.Connections)
    .RequireAuthorization(WellSitePolicies.ConnectionsManage)
    .Produces<ConnectionResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

v1.MapPut(
    "/connections/{connectionId:guid}",
    async (Guid connectionId, UpdateConnectionRequest request, ConnectionService connections, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
        ConnectionMapping.ToResponse(await connections.UpdateAsync(connectionId,
            new UpdateConnectionCommand(request.ExpectedRevision, request.Name, request.Settings),
            access.Subject, access.CreateActor(context), cancellationToken)))
    .WithName("UpdateConnection")
    .NotifiesChange(ChangeArea.Connections)
    .RequireAuthorization(WellSitePolicies.ConnectionsManage)
    .Produces<ConnectionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

v1.MapPut(
    "/connections/{connectionId:guid}/enabled",
    async (Guid connectionId, SetConnectionEnabledRequest request, ConnectionService connections,
        RequestAccessContext access, HttpContext context, CancellationToken cancellationToken) =>
        ConnectionMapping.ToResponse(await connections.SetEnabledAsync(connectionId, request.ExpectedRevision,
            request.Enabled, access.Subject, access.CreateActor(context), cancellationToken)))
    .WithName("SetConnectionEnabled")
    .NotifiesChange(ChangeArea.Connections)
    .RequireAuthorization(WellSitePolicies.ConnectionsManage)
    .Produces<ConnectionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapPost(
    "/connections/{connectionId:guid}/test",
    async (Guid connectionId, ConnectionService connections, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
        ConnectionMapping.ToResponse(await connections.TestAsync(connectionId, access.Subject,
            access.CreateActor(context), cancellationToken)))
    .WithName("TestConnection")
    .NotifiesChange(ChangeArea.Connections)
    .RequireAuthorization(WellSitePolicies.ConnectionsManage)
    .Produces<ConnectionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

v1.MapPost(
    "/connections/{connectionId:guid}/discover",
    async (Guid connectionId, ConnectionService connections, ConnectionImportService imports, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
    {
        var snapshot = await connections.FindAsync(connectionId, access.Subject, access.CreateActor(context), cancellationToken);
        return ConnectionMapping.ToResponse(snapshot, await imports.HierarchyStatusAsync(connectionId, cancellationToken));
    })
    .WithName("DiscoverConnection")
    .RequireAuthorization(WellSitePolicies.ConnectionsManage)
    .Produces<DiscoveryResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

v1.MapPost(
    "/connections/{connectionId:guid}/discover/search",
    (Guid connectionId, DiscoverySearchRequest request, ConnectionService connections, RequestAccessContext access) =>
    {
        var snapshot = connections.GetSnapshot(connectionId, request.SnapshotId, access.Subject);
        var scope = ConnectionMapping.ToSelection(request.SnapshotId, request.Query, request.Facets).Scope;
        return ConnectionMapping.ToResponse(DiscoveryScopes.Apply(snapshot.Result.Items, scope, snapshot.Fields), request.Offset, request.Limit);
    })
    .WithName("SearchDiscoveredObjects")
    .RequireAuthorization(WellSitePolicies.ConnectionsManage)
    .Produces<DiscoverySearchResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapPost("/connections/{connectionId:guid}/source-checks",
    async (Guid connectionId, SourceChangeService changes, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
        await changes.CheckAsync(connectionId, access.Subject, access.CreateActor(context), cancellationToken))
    .WithName("CheckConnectionSourceChanges")
    .RequireAuthorization(WellSitePolicies.ConnectionsManage)
    .Produces<SourceCheckSummary>()
    .Produces<WellSiteProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json");

v1.MapGet("/connections/{connectionId:guid}/source-changes",
    async (Guid connectionId, int? offset, int? limit, string? query, SourceChangeService changes,
        RequestAccessContext access, CancellationToken cancellationToken) =>
        await changes.ReadAsync(connectionId, offset ?? 0, limit ?? 25, query, access.Subject, cancellationToken))
    .WithName("ReviewConnectionSourceChanges")
    .RequireAuthorization(WellSitePolicies.ConnectionsRead)
    .Produces<SourceChangePage>()
    .Produces<WellSiteProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json");

v1.MapPost(
    "/connections/{connectionId:guid}/import/preview",
    async (Guid connectionId, ImportPreviewRequest request, ConnectionImportService imports, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
        ConnectionMapping.ToResponse(await imports.PreviewAsync(connectionId, request.Levels,
            ConnectionMapping.ToSelection(request.SnapshotId, request.Query, request.Facets, request.Objects), access.Subject,
            access.CreateActor(context), request.AdoptExistingHierarchy, cancellationToken)))
    .WithName("PreviewConnectionImport")
    .RequireAuthorization(WellSitePolicies.ConnectionsManage)
    .Produces<ImportPreviewResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

v1.MapPost(
    "/connections/{connectionId:guid}/import",
    async (Guid connectionId, ImportConnectionRequest request, ConnectionImportService imports, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
        ConnectionMapping.ToResponse(await imports.ImportAsync(connectionId, request.Levels,
            ConnectionMapping.ToSelection(request.SnapshotId, request.Query, request.Facets, request.Objects), request.ExpectedFingerprint,
            access.Subject, access.CreateActor(context), request.AdoptExistingHierarchy, cancellationToken)))
    .WithName("ImportFromConnection")
    .NotifiesChange(ChangeArea.Assets)
    .RequireAuthorization(WellSitePolicies.ConnectionsManage)
    .Produces<ImportResultResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

v1.MapDelete(
    "/connections/{connectionId:guid}",
    async (Guid connectionId, ConnectionService connections, RequestAccessContext access,
        HttpContext context, CancellationToken cancellationToken) =>
    {
        await connections.DeleteAsync(connectionId, access.Subject, access.CreateActor(context), cancellationToken);
        return Results.NoContent();
    })
    .WithName("DeleteConnection")
    .NotifiesChange(ChangeArea.Connections)
    .RequireAuthorization(WellSitePolicies.ConnectionsManage)
    .Produces(StatusCodes.Status204NoContent)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

app.MapHealthChecks("/health/ready").AllowAnonymous();

// Live-update signal (ADR-008): area names only, never data. Deliberately not part of the
// reviewed v1 API group; the hub authenticates with the same scheme as the REST API.
app.MapChangeSignal();

app.Run();

static AssetTypeResponse ToAssetTypeResponse(WellSiteAutoPilot.Domain.Assets.AssetTypeDefinition assetType) => new(
    assetType.Id,
    assetType.Key,
    assetType.DisplayName,
    assetType.SchemaVersion,
    assetType.AttributeSchemaJson,
    assetType.IsActive,
    assetType.CreatedAtUtc,
    assetType.HiddenNote,
    assetType.HiddenAtUtc,
    assetType.HiddenBy);

static AssetResponse ToAssetResponse(WellSiteAutoPilot.Domain.Assets.Asset asset) => new(
    asset.Id,
    asset.AssetTypeId,
    asset.Name,
    asset.ParentAssetId,
    asset.AttributeValuesJson,
    asset.IsActive,
    asset.CreatedAtUtc,
    asset.OwnerConnectionId);

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
        .ToArray(),
    revision.View is null ? null : new ConfiguredLogicViewResponse(revision.View.ViewId, revision.View.Revision,
        revision.View.ConnectionId, revision.View.Role));

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

static WellSiteAutoPilot.Api.Contracts.Control.KillSwitchResponse ToKillSwitchResponse(
    WellSiteAutoPilot.Domain.Control.ControlKillSwitch item,
    WellSiteAutoPilot.Application.Control.AutonomousControlOptions options) =>
    new(item.Engaged, item.ChangedBy, item.ChangedAtUtc, item.Reason, options.Enabled);

static WellSiteAutoPilot.Api.Contracts.Control.AutonomyPolicyResponse ToAutonomyPolicyResponse(
    WellSiteAutoPilot.Domain.Control.AutonomyPolicy item) =>
    new(item.ConfiguredLogicId, item.Enabled, item.SinkKind,
        item.Limits.Select(limit => new WellSiteAutoPilot.Api.Contracts.Control.AutonomyCommandLimitDto(
            limit.Command, limit.Unit, limit.MinimumValue, limit.MaximumValue)).ToArray(),
        item.MaxActionsPerWindow, item.WindowSeconds, item.UpdatedBy, item.UpdatedAtUtc);

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

static ExecutionResponse ToDetailedResponse(ExecutionRecord execution) => ToResponse(execution) with
{
    Provenance = ExecutionProvenanceMapper.Read(execution)
};

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

static AssetOwnerScope? AssetBrowseOwnerScope(Guid? ownerConnectionId, bool? standalone)
{
    if (ownerConnectionId is not null && standalone == true)
        throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
            "Choose either one Connection or the standalone Assets, not both.");
    return ownerConnectionId is { } connectionId ? new AssetOwnerScope(connectionId)
        : standalone == true ? new AssetOwnerScope(null) : null;
}

static int PageOffset(int? offset) => offset is null ? 0 : offset >= 0 && offset <= 100_000 ? offset.Value :
    throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation, "The requested page offset is invalid.");

static string SearchText(string? text) => text?.Trim() is { Length: >= 1 and <= 120 } trimmed ? trimmed :
    throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation, "Search text must be 1 to 120 characters.");

static T? ParseFilter<T>(string? raw) where T : struct, Enum => raw is null ? null :
    Enum.TryParse<T>(raw, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value :
        throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation, "The requested status filter is invalid.");

static ConfiguredLogicResponse WithConfigurationActions(ConfiguredLogicDefinition item, ConfiguredLogicActions? capability) =>
    ToConfiguredLogicResponse(item) with { CanManage = capability?.CanManage == true, CanRequestShadow = capability?.CanRequestShadow == true };

static AccessGrantResponse ToAccessGrantResponse(AccessGrant grant) =>
    new(grant.Id, grant.SubjectKind.ToContract(), grant.UserId,
        grant.GroupId, grant.RoleId, grant.ScopeKind.ToContract(),
        grant.AssetIds.ToArray(), grant.IsActive, grant.Revision, grant.DirectoryId, grant.GroupDisplayName, grant.GroupDomain);

public partial class Program;
