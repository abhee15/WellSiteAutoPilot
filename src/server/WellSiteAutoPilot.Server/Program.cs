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
using WellSiteAutoPilot.Server.Scheduling;
using WellSiteAutoPilot.Server.Security;
using WellSiteAutoPilot.Server.Contracts;

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
builder.Services.AddScoped<RecommendationDecisionService>();
builder.Services.AddScoped<RecommendationQueryService>();
builder.Services.AddScoped<UserAccessService>();
builder.Services.AddScoped<RoleCatalogService>();
builder.Services.AddScoped<AccessGrantAdministrationService>();
builder.Services.AddScoped<ScopedAccessInspectionService>();
builder.Services.AddScoped<ProposedAssetAccessService>();
builder.Services.AddScoped<LegacyMigrationPreflightService>();
builder.Services.AddScoped<DeploymentMigrationPreflightService>();
builder.Services.AddScoped<LegacyGrantMigrationService>();
builder.Services.AddScoped<AssetScopeAuthorizer>();
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
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        var roles = ParseApplicationRoles(request.Roles);
        var updated = await userAccessService.ReplaceAccessAsync(
            userId,
            roles,
            request.AssetScopeIds ?? [],
            new SecurityActorContext(
                GetCurrentUserId(httpContext.User),
                httpContext.User.Identity?.Name ?? "unknown",
                CorrelationContext.GetCorrelationId(httpContext)),
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

// Role authoring is Admin-only until scoped authorization and constrained
// security delegation are implemented. New custom roles have no live access
// effect until a reviewed assignment is created in the entitlement model.
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
    async (int? limit, RoleCatalogService catalog, CancellationToken cancellationToken) =>
        (await catalog.ListAsync(limit ?? 100, cancellationToken))
        .Select(ToRoleResponse).ToArray())
    .WithName("ListSecurityRoles")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<RoleDefinitionResponse[]>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status403Forbidden);

v1.MapGet(
    "/security/roles/{roleId:guid}",
    async (Guid roleId, RoleCatalogService catalog, CancellationToken cancellationToken) =>
        ToRoleResponse(await catalog.GetRequiredAsync(roleId, cancellationToken)))
    .WithName("GetSecurityRole")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<RoleDefinitionResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

v1.MapPost(
    "/security/roles",
    async (
        CreateSecurityRoleRequest request,
        RoleCatalogService catalog,
        HttpContext context,
        CancellationToken cancellationToken) =>
    {
        var role = await catalog.CreateAsync(
            new RoleMutationCommand(request.Name, request.Description,
                request.Permissions, true),
            new SecurityActorContext(
                GetCurrentUserId(context.User),
                context.User.Identity?.Name ?? "unknown",
                CorrelationContext.GetCorrelationId(context)),
            cancellationToken);

        return Results.Created(
            $"/api/v1/security/roles/{role.Id}",
            ToRoleResponse(role));
    })
    .WithName("CreateSecurityRole")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<RoleDefinitionResponse>(StatusCodes.Status201Created)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

v1.MapPut(
    "/security/roles/{roleId:guid}",
    async (
        Guid roleId,
        UpdateSecurityRoleRequest request,
        RoleCatalogService catalog,
        HttpContext context,
        CancellationToken cancellationToken) =>
    {
        var role = await catalog.UpdateAsync(
            roleId,
            request.ExpectedRevision,
            new RoleMutationCommand(request.Name, request.Description,
                request.Permissions, request.IsActive),
            new SecurityActorContext(
                GetCurrentUserId(context.User),
                context.User.Identity?.Name ?? "unknown",
                CorrelationContext.GetCorrelationId(context)),
            cancellationToken);
        return ToRoleResponse(role);
    })
    .WithName("UpdateSecurityRole")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<RoleDefinitionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

// Administrator-only rollback of grants created by a legacy migration.
// Only server-audited migration-origin rows are affected, never customer
// assignments, compiled legacy RBAC or production runtime permissions.
v1.MapPost(
    "/security/rollback-legacy-grants",
    async (
        RollbackLegacyGrantsRequest request,
        LegacyGrantMigrationService migration,
        HttpContext context,
        CancellationToken cancellationToken) =>
    {
        var result = await migration.RollbackAsync(
            request.Confirmed,
            new SecurityActorContext(
                GetCurrentUserId(context.User),
                context.User.Identity?.Name ?? "unknown",
                CorrelationContext.GetCorrelationId(context)),
            cancellationToken);
        return new RollbackLegacyGrantsResponse(
            result.MigratedGrantsInspected, result.GrantsDeactivated,
            result.LiveAuthorizationUnchanged);
    })
    .WithName("RollbackLegacyAccessGrants")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<RollbackLegacyGrantsResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status409Conflict, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

// Explicit, audited grant materialization from the current legacy model.
// Performs complete serializable transaction parity validation and declines
// conflicting active grants. It NEVER changes LIVE authorization policies.
v1.MapPost(
    "/security/materialize-legacy-grants",
    async (
        MaterializeLegacyGrantsRequest request,
        LegacyGrantMigrationService migration,
        HttpContext context,
        CancellationToken cancellationToken) =>
    {
        var result = await migration.MigrateAsync(
            request.Confirmed,
            new SecurityActorContext(
                GetCurrentUserId(context.User),
                context.User.Identity?.Name ?? "unknown",
                CorrelationContext.GetCorrelationId(context)),
            cancellationToken);
        return new MaterializeLegacyGrantsResponse(
            result.ActiveUsersInspected, result.ActiveAssetsInspected,
            result.GrantsCreated, result.UsersAlreadyMatching,
            result.RecordedGrantsMatchLegacy,
            result.LiveAuthorizationUnchanged);
    })
    .WithName("MaterializeLegacyAccessGrants")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<MaterializeLegacyGrantsResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status409Conflict, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

// Complete bounded deployment parity preview, using one consistent
// database snapshot across ALL users, roles, grants and Assets. Admin-only,
// read-only, and never authorization cutover approval.
v1.MapGet(
    "/security/migration-preflight",
    async (
        DeploymentMigrationPreflightService preflight,
        HttpContext context,
        CancellationToken cancellationToken) =>
    {
        var result = await preflight.InspectAsync(
            new SecurityActorContext(
                GetCurrentUserId(context.User),
                context.User.Identity?.Name ?? "unknown",
                CorrelationContext.GetCorrelationId(context)),
            cancellationToken);
        return new DeploymentMigrationPreflightResponse(
            result.ActiveUserCount, result.ActiveAssetCount,
            result.MatchingUserCount, result.ExpansionCount,
            result.ReductionCount,
            result.Users.Select(item => new DeploymentMigrationUserResponse(
                item.UserId, item.ExpectedLegacyGrantCount,
                item.RecordedGrantCount, item.ExpansionCount,
                item.ReductionCount, item.RecordedGrantsMatchLegacy)).ToArray(),
            result.AllActiveUsersMatchRecordedGrants,
            result.IsDiagnosticOnly);
    })
    .WithName("InspectDeploymentLegacyMigration")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<DeploymentMigrationPreflightResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

// Migration preflight captures roles, direct grants, the user and the full
// bounded active Asset inventory under a single PostgreSQL snapshot. It is
// read-only, one user at a time, and NEVER enables authorization cutover.
v1.MapGet(
    "/security/users/{userId:guid}/migration-preflight",
    async (
        Guid userId,
        LegacyMigrationPreflightService preflight,
        HttpContext context,
        CancellationToken cancellationToken) =>
    {
        var result = await preflight.InspectAsync(
            userId,
            new SecurityActorContext(
                GetCurrentUserId(context.User),
                context.User.Identity?.Name ?? "unknown",
                CorrelationContext.GetCorrelationId(context)),
            cancellationToken);
        return new LegacyMigrationPreflightResponse(
            result.UserId, result.ActiveAssetCount,
            result.ExpectedLegacyGrantCount, result.RecordedGrantCount,
            result.ExpansionCount, result.ReductionCount,
            result.DifferenceSamples.Select(item =>
                new LegacyMigrationDifferenceResponse(
                    item.AssetId, item.Permission,
                    item.LegacyAllowed, item.RecordedGrantAllowed)).ToArray(),
            result.RecordedGrantsMatchLegacy, result.IsDiagnosticOnly);
    })
    .WithName("InspectLegacyGrantMigration")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<LegacyMigrationPreflightResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

// Proposed resource preview uses one database query that restricts Assets
// by the same grant's active role and scope BEFORE Skip/Take. It never
// authorizes live requests or changes existing user access.
v1.MapGet(
    "/security/users/{userId:guid}/proposed-assets",
    async (
        Guid userId,
        string permission,
        int? offset,
        int? limit,
        ProposedAssetAccessService preview,
        HttpContext context,
        CancellationToken cancellationToken) =>
    {
        var result = await preview.ListAsync(
            userId, permission, offset ?? 0, limit ?? 100,
            new SecurityActorContext(
                GetCurrentUserId(context.User),
                context.User.Identity?.Name ?? "unknown",
                CorrelationContext.GetCorrelationId(context)),
            cancellationToken);
        return new ProposedAssetAccessResponse(
            result.UserId, result.Permission, result.Offset, result.Limit,
            result.Assets.Select(item =>
                new ProposedAuthorizedAssetResponse(item.Id, item.Name)).ToArray(),
            result.IsDiagnosticOnly);
    })
    .WithName("ListProposedAuthorizedAssets")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<ProposedAssetAccessResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

// Read-only diagnostic: evaluate every registered permission against
// legacy live RBAC and the stored, inactive-for-runtime scoped-grant model.
// A single Asset inspection never certifies whole-deployment migration parity.
v1.MapGet(
    "/security/users/{userId:guid}/effective-access",
    async (
        Guid userId,
        Guid? assetId,
        ScopedAccessInspectionService inspector,
        HttpContext context,
        CancellationToken cancellationToken) =>
    {
        var inspection = await inspector.InspectAsync(
            userId, assetId,
            new SecurityActorContext(
                GetCurrentUserId(context.User),
                context.User.Identity?.Name ?? "unknown",
                CorrelationContext.GetCorrelationId(context)),
            cancellationToken);
        return new ScopedAccessInspectionResponse(
            inspection.UserId,
            inspection.AssetId,
            inspection.AssetName,
            inspection.Permissions.Select(item =>
                new PermissionAccessComparisonResponse(
                    item.Permission, item.ScopeKind,
                    item.LegacyAllowed, item.ProposedAllowed,
                    item.ProposedReasonCode, item.ProposedGrantId)).ToArray(),
            inspection.WideningCount, inspection.ReductionCount,
            inspection.IsDiagnosticOnly);
    })
    .WithName("InspectUserEffectiveAccess")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<ScopedAccessInspectionResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status404NotFound, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);

// Additive grants are recorded and audited but do not alter legacy
// authorization until the parity-verified entitlement cutover is complete.
v1.MapGet(
    "/security/users/{userId:guid}/grants",
    async (
        Guid userId,
        AccessGrantAdministrationService grants,
        HttpContext context,
        CancellationToken cancellationToken) =>
        (await grants.ListForUserAsync(
            userId,
            new SecurityActorContext(
                GetCurrentUserId(context.User),
                context.User.Identity?.Name ?? "unknown",
                CorrelationContext.GetCorrelationId(context)),
            cancellationToken))
        .Select(ToAccessGrantResponse).ToArray())
    .WithName("ListUserAccessGrants")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<AccessGrantResponse[]>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status403Forbidden);

v1.MapPost(
    "/security/users/{userId:guid}/grants",
    async (
        Guid userId,
        CreateUserAccessGrantRequest request,
        AccessGrantAdministrationService grants,
        HttpContext context,
        CancellationToken cancellationToken) =>
    {
        if (!Enum.TryParse<AccessGrantScopeKind>(
                request.ScopeKind, ignoreCase: false, out var scope) ||
            !Enum.IsDefined(scope))
        {
            throw new WellSiteAutoPilotException(
                FailureCodes.ValidationFailed, FailureKind.Validation,
                "An explicit supported grant scope kind is required.");
        }

        var result = await grants.CreateForUserAsync(
            userId,
            new CreateAccessGrantCommand(
                request.RoleId, scope, request.AssetIds ?? [], request.ExpectedRoleRevision),
            new SecurityActorContext(
                GetCurrentUserId(context.User),
                context.User.Identity?.Name ?? "unknown",
                CorrelationContext.GetCorrelationId(context)),
            cancellationToken);

        return Results.Created(
            $"/api/v1/security/users/{userId}/grants",
            ToAccessGrantResponse(result));
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
    async (
        Guid userId,
        Guid grantId,
        DeactivateUserAccessGrantRequest request,
        AccessGrantAdministrationService grants,
        HttpContext context,
        CancellationToken cancellationToken) =>
        ToAccessGrantResponse(await grants.DeactivateForUserAsync(
            userId, grantId, request.ExpectedRevision,
            new SecurityActorContext(
                GetCurrentUserId(context.User),
                context.User.Identity?.Name ?? "unknown",
                CorrelationContext.GetCorrelationId(context)),
            cancellationToken)))
    .WithName("DeactivateUserAccessGrant")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<AccessGrantResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")
    .Produces<WellSiteProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
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
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        if (request.ParentAssetId is Guid parentAssetId)
        {
            assetScopeAuthorizer.RequireAsset(
                httpContext.User,
                parentAssetId);
        }
        else
        {
            assetScopeAuthorizer.RequireAdministrator(httpContext.User);
        }

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
    "/security/assets/directory",
    async (
        int? offset,
        int? limit,
        AssetService assetService,
        AssetScopeAuthorizer assetScopeAuthorizer,
        AccessGrantAdministrationService grants,
        HttpContext context,
        CancellationToken cancellationToken) =>
    {
        assetScopeAuthorizer.RequireAdministrator(context.User);
        // Recheck current persisted administrator status on every full-directory
        // read; a stale identity claim must not survive role revocation.
        await grants.RequireAdministratorAsync(
            new SecurityActorContext(
                GetCurrentUserId(context.User),
                context.User.Identity?.Name ?? "unknown",
                CorrelationContext.GetCorrelationId(context)),
            cancellationToken);
        var assets = await assetService.ListAssetDirectoryPageAsync(
            offset ?? 0, limit ?? 200, cancellationToken);
        return assets.Select(asset => new AssetDirectoryEntryResponse(
            asset.Id, asset.Name, asset.IsActive)).ToArray();
    })
    .WithName("ListSecurityAssetDirectory")
    .RequireAuthorization(WellSitePolicies.SecurityManage)
    .Produces<AssetDirectoryEntryResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")
    .Produces(StatusCodes.Status403Forbidden);


v1.MapGet(
    "/assets",
    async (
        Guid? assetTypeId,
        Guid? parentAssetId,
        int? limit,
        AssetService assetService,
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        var allowedAssetIds =
            assetScopeAuthorizer.GetAllowedAssetIds(httpContext.User);

        var assets = allowedAssetIds is null
            ? await assetService.ListAssetsAsync(
                assetTypeId,
                parentAssetId,
                limit ?? 100,
                cancellationToken)
            : await assetService.ListAssetsInScopeAsync(
                assetTypeId,
                parentAssetId,
                limit ?? 100,
                allowedAssetIds,
                cancellationToken);

        return assets.Select(ToAssetResponse).ToArray();
    })
    .WithName("ListAssets")
    .RequireAuthorization(WellSitePolicies.AssetsRead)
    .Produces<AssetResponse[]>(StatusCodes.Status200OK);

v1.MapGet(
    "/assets/{assetId:guid}",
    async (
        Guid assetId,
        AssetService assetService,
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        assetScopeAuthorizer.RequireAsset(
            httpContext.User,
            assetId);

        return ToAssetResponse(
            await assetService.GetRequiredAssetAsync(
                assetId,
                cancellationToken));
    })
    .WithName("GetAsset")
    .RequireAuthorization(WellSitePolicies.AssetsRead)
    .Produces<AssetResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json");

v1.MapPost(
    "/configured-logic",
    async (
        CreateConfiguredLogicRequest request,
        ConfiguredLogicService service,
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        assetScopeAuthorizer.RequireAssets(
            httpContext.User,
            request.AssetBindings.Select(binding => binding.AssetId));

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
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        var allowedAssetIds =
            assetScopeAuthorizer.GetAllowedAssetIds(httpContext.User);

        var configuredLogic = allowedAssetIds is null
            ? await service.ListAsync(
                limit ?? 100,
                cancellationToken)
            : await service.ListInScopeAsync(
                limit ?? 100,
                allowedAssetIds,
                cancellationToken);

        return configuredLogic
            .Select(ToConfiguredLogicResponse)
            .ToArray();
    })
    .WithName("ListConfiguredLogic")
    .RequireAuthorization(WellSitePolicies.ConfiguredLogicRead)
    .Produces<ConfiguredLogicResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json");

v1.MapGet(
    "/configured-logic/{configuredLogicId:guid}",
    async (
        Guid configuredLogicId,
        ConfiguredLogicService service,
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        var allowedAssetIds =
            assetScopeAuthorizer.GetAllowedAssetIds(httpContext.User);

        var configuredLogic = allowedAssetIds is null
            ? await service.GetRequiredAsync(
                configuredLogicId,
                cancellationToken)
            : await service.GetRequiredInScopeAsync(
                configuredLogicId,
                allowedAssetIds,
                cancellationToken);

        return ToConfiguredLogicResponse(configuredLogic);
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
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        var allowedAssetIds =
            assetScopeAuthorizer.GetAllowedAssetIds(httpContext.User);

        if (allowedAssetIds is not null)
        {
            _ = await service.GetRequiredInScopeAsync(
                configuredLogicId,
                allowedAssetIds,
                cancellationToken);

            assetScopeAuthorizer.RequireAssets(
                httpContext.User,
                request.AssetBindings.Select(binding => binding.AssetId));
        }

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
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        var allowedAssetIds =
            assetScopeAuthorizer.GetAllowedAssetIds(httpContext.User);

        if (allowedAssetIds is not null)
        {
            _ = await service.GetRequiredInScopeAsync(
                configuredLogicId,
                allowedAssetIds,
                cancellationToken);
        }

        return ToConfiguredLogicRevisionResponse(
            await service.ValidateRevisionAsync(
                configuredLogicId,
                revisionId,
                cancellationToken));
    })
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
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        var allowedAssetIds =
            assetScopeAuthorizer.GetAllowedAssetIds(httpContext.User);

        if (allowedAssetIds is not null)
        {
            _ = await service.GetRequiredInScopeAsync(
                configuredLogicId,
                allowedAssetIds,
                cancellationToken);
        }

        return ToConfiguredLogicRevisionResponse(
            await service.ActivateRevisionAsync(
                configuredLogicId,
                revisionId,
                cancellationToken));
    })
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
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        var allowedAssetIds =
            assetScopeAuthorizer.GetAllowedAssetIds(httpContext.User);

        var executions = allowedAssetIds is null
            ? await executionService.ListAsync(
                status,
                limit ?? 50,
                cancellationToken)
            : await executionService.ListInScopeAsync(
                status,
                limit ?? 50,
                allowedAssetIds,
                cancellationToken);

        return executions.Select(ToResponse).ToArray();
    })
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
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        assetScopeAuthorizer.RequireAsset(
            httpContext.User,
            request.AssetId);

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
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        var allowedAssetIds =
            assetScopeAuthorizer.GetAllowedAssetIds(httpContext.User);

        var execution = allowedAssetIds is null
            ? await executionService.GetRequiredAsync(
                executionId,
                cancellationToken)
            : await executionService.GetRequiredInScopeAsync(
                executionId,
                allowedAssetIds,
                cancellationToken);

        return ToResponse(execution);
    })
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

v1.MapGet(
    "/recommendations",
    async (
        string? status,
        int? limit,
        RecommendationQueryService queryService,
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        var allowedAssetIds =
            assetScopeAuthorizer.GetAllowedAssetIds(httpContext.User);

        var recommendations = await queryService.ListAsync(
            status,
            limit ?? 50,
            allowedAssetIds,
            cancellationToken);

        return recommendations.Select(ToRecommendationSummaryResponse).ToArray();
    })
    .WithName("ListRecommendations")
    .RequireAuthorization(WellSitePolicies.RecommendationsRead)
    .Produces<RecommendationSummaryResponse[]>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status400BadRequest, "application/problem+json");

v1.MapGet(
    "/recommendations/{recommendationId:guid}",
    async (
        Guid recommendationId,
        RecommendationQueryService queryService,
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        var allowedAssetIds =
            assetScopeAuthorizer.GetAllowedAssetIds(httpContext.User);

        var recommendation = await queryService.GetRequiredAsync(
            recommendationId,
            allowedAssetIds,
            cancellationToken);

        return ToRecommendationSummaryResponse(recommendation);
    })
    .WithName("GetRecommendation")
    .RequireAuthorization(WellSitePolicies.RecommendationsRead)
    .Produces<RecommendationSummaryResponse>(StatusCodes.Status200OK)
    .Produces<WellSiteProblemDetails>(
        StatusCodes.Status404NotFound, "application/problem+json");

v1.MapPost(
    "/recommendations/{recommendationId:guid}/decision",
    async (
        Guid recommendationId,
        RecommendationDecisionRequest request,
        RecommendationDecisionService decisionService,
        AssetScopeAuthorizer assetScopeAuthorizer,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
    {
        if (!Enum.TryParse<RecommendationStatus>(request.Decision, true, out var decision) ||
            decision is not (RecommendationStatus.Approved or RecommendationStatus.Rejected))
        {
            throw new WellSiteAutoPilotException(
                "RECOMMENDATION_DECISION_INVALID", FailureKind.Validation,
                "Decision must be Approved or Rejected.");
        }

        var recommendation = await decisionService.DecideAsync(
            recommendationId,
            decision,
            httpContext.User.Identity?.Name ?? string.Empty,
            request.Reason,
            assetScopeAuthorizer.GetAllowedAssetIds(httpContext.User),
            cancellationToken);

        return new RecommendationDecisionResponse(
            recommendation.Id,
            recommendation.Status.ToString(),
            recommendation.DecisionBy,
            recommendation.DecisionAtUtc,
            recommendation.DecisionReason);
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
    recommendation.Status.ToString(),
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
            FailureKind.Authorization,
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

static AccessGrantResponse ToAccessGrantResponse(AccessGrant grant) =>
    new(grant.Id, grant.SubjectKind.ToString(), grant.UserId,
        grant.GroupSid, grant.RoleId, grant.ScopeKind.ToString(),
        grant.AssetIds.ToArray(), grant.IsActive, grant.Revision);

public partial class Program;
