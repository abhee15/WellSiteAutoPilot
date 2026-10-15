using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Security;

var connectionString = Environment.GetEnvironmentVariable(
    "WSA_DATABASE_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "WSA_DATABASE_CONNECTION_STRING is required.");
}

var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var dbContext = new WellSiteAutoPilotDbContext(options);
var clock = new FixedTimeProvider(
    new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
var repository = new UserAccessRepository(dbContext);
var service = new UserAccessService(repository, clock);

var assetService = new AssetService(
    new AssetRepository(dbContext),
    clock);

var assetType = await assetService.CreateAssetTypeAsync(
    new CreateAssetTypeCommand(
        $"security-well-{Guid.NewGuid():N}",
        "Security Test Well",
        """{"type":"object"}"""));

var asset = await assetService.CreateAssetAsync(
    new CreateAssetCommand(
        assetType.Id,
        "Security Well 101",
        null,
        "{}"));

var otherAsset = await assetService.CreateAssetAsync(
    new CreateAssetCommand(
        assetType.Id,
        "Security Well 202",
        null,
        "{}"));

var scopedAssets = await assetService.ListAssetsInScopeAsync(
    null,
    null,
    100,
    [asset.Id]);

if (scopedAssets.Count != 1 ||
    scopedAssets.Single().Id != asset.Id)
{
    throw new InvalidOperationException(
        "Asset query did not isolate the authorized Asset scope.");
}

var inScopeConfiguredLogicId = Guid.NewGuid();
var inScopeRevisionId = Guid.NewGuid();
var outOfScopeConfiguredLogicId = Guid.NewGuid();
var outOfScopeRevisionId = Guid.NewGuid();

dbContext.ConfiguredLogicDefinitions.AddRange(
    new ConfiguredLogicEntity
    {
        Id = inScopeConfiguredLogicId,
        Name = "Scoped Configured Logic",
        CreatedAtUtc = clock.GetUtcNow()
    },
    new ConfiguredLogicEntity
    {
        Id = outOfScopeConfiguredLogicId,
        Name = "Out Of Scope Configured Logic",
        CreatedAtUtc = clock.GetUtcNow()
    });

dbContext.ConfiguredLogicRevisions.AddRange(
    new ConfiguredLogicRevisionEntity
    {
        Id = inScopeRevisionId,
        ConfiguredLogicId = inScopeConfiguredLogicId,
        RevisionNumber = 1,
        ModuleId = "security.scope-test",
        ModuleVersion = "1.0.0",
        ModuleManifestJson = "{}",
        Mode = nameof(ExecutionMode.Shadow),
        ParametersJson = "{}",
        Status = nameof(ConfiguredLogicRevisionStatus.Draft),
        CreatedAtUtc = clock.GetUtcNow()
    },
    new ConfiguredLogicRevisionEntity
    {
        Id = outOfScopeRevisionId,
        ConfiguredLogicId = outOfScopeConfiguredLogicId,
        RevisionNumber = 1,
        ModuleId = "security.scope-test",
        ModuleVersion = "1.0.0",
        ModuleManifestJson = "{}",
        Mode = nameof(ExecutionMode.Shadow),
        ParametersJson = "{}",
        Status = nameof(ConfiguredLogicRevisionStatus.Draft),
        CreatedAtUtc = clock.GetUtcNow()
    });

dbContext.ConfiguredLogicAssetBindings.AddRange(
    new ConfiguredLogicAssetBindingEntity
    {
        RevisionId = inScopeRevisionId,
        Role = "well",
        AssetId = asset.Id,
        ParameterOverridesJson = "{}"
    },
    new ConfiguredLogicAssetBindingEntity
    {
        RevisionId = outOfScopeRevisionId,
        Role = "well",
        AssetId = otherAsset.Id,
        ParameterOverridesJson = "{}"
    });

await dbContext.SaveChangesAsync();

var configuredLogicRepository =
    new ConfiguredLogicRepository(dbContext);
var scopedConfiguredLogic =
    await configuredLogicRepository.ListInScopeAsync(
        100,
        [asset.Id]);

if (scopedConfiguredLogic.Count != 1 ||
    scopedConfiguredLogic.Single().Id != inScopeConfiguredLogicId ||
    await configuredLogicRepository.GetInScopeAsync(
        outOfScopeConfiguredLogicId,
        [asset.Id]) is not null)
{
    throw new InvalidOperationException(
        "Configured Logic query did not isolate the authorized Asset scope.");
}

var executionRepository = new ExecutionRepository(dbContext);
var executionService = new ExecutionService(
    executionRepository,
    clock);

var inScopeExecution = await executionService.RequestShadowAsync(
    new ShadowExecutionCommand(
        Guid.NewGuid(),
        "security.scope-test",
        "1.0.0",
        Guid.NewGuid(),
        asset.Id,
        "WELL-101",
        "PumpFillage"),
    "security-scope-in");

var outOfScopeExecution = await executionService.RequestShadowAsync(
    new ShadowExecutionCommand(
        Guid.NewGuid(),
        "security.scope-test",
        "1.0.0",
        Guid.NewGuid(),
        otherAsset.Id,
        "WELL-202",
        "PumpFillage"),
    "security-scope-out");

var scopedExecutions = await executionService.ListInScopeAsync(
    null,
    100,
    [asset.Id]);

if (!scopedExecutions.Any(item => item.Id == inScopeExecution.Id) ||
    scopedExecutions.Any(item => item.Id == outOfScopeExecution.Id) ||
    await executionRepository.GetInScopeAsync(
        outOfScopeExecution.Id,
        [asset.Id]) is not null)
{
    throw new InvalidOperationException(
        "Execution query did not isolate the authorized Asset scope.");
}

var administratorIdentityKey =
    UserAccessService.NormalizeIdentityKey(
        "SID:S-1-5-21-1000-1000-1000-1001");
var operatorIdentityKey =
    UserAccessService.NormalizeIdentityKey(
        "SID:S-1-5-21-1000-1000-1000-1002");
var secondAdministratorIdentityKey =
    UserAccessService.NormalizeIdentityKey(
        "SID:S-1-5-21-1000-1000-1000-1003");

var bootstrapIdentity =
    UserAccessService.NormalizeIdentity(@"FIELD\wsa-admin");
var bootstrap = new HashSet<string>(
    [bootstrapIdentity],
    StringComparer.Ordinal);

var administrator = await service.ResolveAuthenticatedAsync(
    administratorIdentityKey,
    @"FIELD\wsa-admin",
    "WSA Administrator",
    bootstrap);

if (!administrator.Roles.Contains(ApplicationRole.Admin) ||
    !RolePermissionMap.HasPermission(
        administrator.Roles,
        WellSitePermissions.SecurityManage))
{
    throw new InvalidOperationException(
        "Configured bootstrap administrator did not receive Admin access.");
}

clock.Advance(TimeSpan.FromMinutes(1));

var sameAdministrator = await service.ResolveAuthenticatedAsync(
    administratorIdentityKey,
    @"FIELD\wsa-admin-renamed",
    "WSA Administrator Renamed",
    bootstrap);

if (sameAdministrator.Id != administrator.Id ||
    sameAdministrator.IdentityKey != administrator.IdentityKey ||
    sameAdministrator.IdentityName != @"FIELD\wsa-admin-renamed" ||
    sameAdministrator.LastSeenAtUtc != administrator.LastSeenAtUtc)
{
    throw new InvalidOperationException(
        "Stable Windows identity did not survive account rename or last-seen throttling wrote too early.");
}

clock.Advance(TimeSpan.FromMinutes(5));

sameAdministrator = await service.ResolveAuthenticatedAsync(
    administratorIdentityKey,
    @"FIELD\wsa-admin-renamed",
    "WSA Administrator Renamed",
    bootstrap);

if (sameAdministrator.LastSeenAtUtc <= administrator.LastSeenAtUtc ||
    sameAdministrator.Roles.Count != 1 ||
    !sameAdministrator.Roles.Contains(ApplicationRole.Admin))
{
    throw new InvalidOperationException(
        "Stable Windows identity lost authorization or last-seen state was not refreshed after the write interval.");
}

administrator = sameAdministrator;

var operatorUser = await service.ResolveAuthenticatedAsync(
    operatorIdentityKey,
    @"FIELD\operator-one",
    "Operator One",
    bootstrap);

if (operatorUser.Roles.Count != 0)
{
    throw new InvalidOperationException(
        "Non-bootstrap first access unexpectedly received application roles.");
}

operatorUser = await service.ReplaceAccessAsync(
    operatorUser.Id,
    [ApplicationRole.Operator],
    [asset.Id],
    new SecurityActorContext(
        administrator.Id,
        administrator.IdentityName,
        "security-test-operator-access"));

if (!operatorUser.Roles.SequenceEqual([ApplicationRole.Operator]) ||
    !operatorUser.AssetScopeIds.SequenceEqual([asset.Id]) ||
    !operatorUser.HasAssetAccess(asset.Id) ||
    RolePermissionMap.HasPermission(
        operatorUser.Roles,
        WellSitePermissions.AssetsManage) ||
    !RolePermissionMap.HasPermission(
        operatorUser.Roles,
        WellSitePermissions.RecommendationsDecide))
{
    throw new InvalidOperationException(
        "Operator RBAC or Asset scope mapping is incorrect.");
}

try
{
    await service.ReplaceAccessAsync(
        operatorUser.Id,
        [ApplicationRole.Operator],
        [Guid.NewGuid()],
        new SecurityActorContext(
            administrator.Id,
            administrator.IdentityName,
            "security-test-invalid-scope"));

    throw new InvalidOperationException(
        "Invalid Asset scope assignment was accepted.");
}
catch (InvalidOperationException exception)
    when (exception.Message.Contains(
        "active Assets",
        StringComparison.Ordinal))
{
}

try
{
    await service.ReplaceAccessAsync(
        administrator.Id,
        [ApplicationRole.Engineer],
        [],
        new SecurityActorContext(
            administrator.Id,
            administrator.IdentityName,
            "security-test-last-admin"));

    throw new InvalidOperationException(
        "The final Admin role was removed.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_SELF_ACCESS_CHANGE_FORBIDDEN")
{
}

var secondAdministrator = await service.ResolveAuthenticatedAsync(
    secondAdministratorIdentityKey,
    @"FIELD\wsa-admin-two",
    "WSA Administrator Two",
    new HashSet<string>(
        [UserAccessService.NormalizeIdentity(@"FIELD\wsa-admin-two")],
        StringComparer.Ordinal));

if (secondAdministrator.Roles.Count != 0)
{
    throw new InvalidOperationException(
        "Bootstrap configuration elevated a second user while an active Admin already existed.");
}

secondAdministrator = await service.ReplaceAccessAsync(
    secondAdministrator.Id,
    [ApplicationRole.Admin],
    [],
    new SecurityActorContext(
        administrator.Id,
        administrator.IdentityName,
        "security-test-second-admin"));

await service.ReplaceAccessAsync(
    administrator.Id,
    [ApplicationRole.Engineer],
    [asset.Id],
    new SecurityActorContext(
        secondAdministrator.Id,
        secondAdministrator.IdentityName,
        "security-test-admin-replacement"));

var updatedAdministrator = await service.GetRequiredAsync(
    administrator.Id);

updatedAdministrator = await service.ResolveAuthenticatedAsync(
    administratorIdentityKey,
    @"FIELD\wsa-admin",
    administrator.DisplayName,
    bootstrap);

if (updatedAdministrator.Roles.Contains(ApplicationRole.Admin) ||
    !updatedAdministrator.Roles.Contains(ApplicationRole.Engineer) ||
    secondAdministrator.Roles.Count != 1 ||
    !secondAdministrator.Roles.Contains(ApplicationRole.Admin))
{
    throw new InvalidOperationException(
        "One-time bootstrap or Admin replacement invariant behaved incorrectly.");
}

var auditEvents = await dbContext.AuditEvents
    .AsNoTracking()
    .OrderBy(item => item.OccurredAtUtc)
    .ToArrayAsync();

if (!auditEvents.Any(
        item => item.Action == "security.user.provisioned" &&
                item.TargetId == administrator.Id.ToString()) ||
    !auditEvents.Any(
        item => item.Action == "security.bootstrap-admin.assigned" &&
                item.TargetId == administrator.Id.ToString() &&
                item.ActorIdentity == "system:bootstrap") ||
    !auditEvents.Any(
        item => item.Action == "security.user.access-replaced" &&
                item.TargetId == operatorUser.Id.ToString() &&
                item.ActorUserId == administrator.Id &&
                item.CorrelationId == "security-test-operator-access") ||
    !auditEvents.Any(
        item => item.Action == "security.user.access-replaced" &&
                item.TargetId == administrator.Id.ToString() &&
                item.ActorUserId == secondAdministrator.Id &&
                item.CorrelationId == "security-test-admin-replacement"))
{
    throw new InvalidOperationException(
        "Security audit trail did not preserve provisioning, bootstrap, and access-change events.");
}

var users = await service.ListAsync(100);
if (users.Count < 3 ||
    users.Select(item => item.IdentityKey).Distinct().Count() !=
    users.Count)
{
    throw new InvalidOperationException(
        "Security user persistence returned duplicate stable identity keys.");
}


// Administrator-defined roles have their own schema and never implicitly
// modify today's per-user RBAC until the paired-grant migration is verified.
var roleCatalog = new RoleCatalogService(
    new RoleCatalogRepository(dbContext), new UserAccessRepository(dbContext), clock);
var actor = new SecurityActorContext(
    secondAdministrator.Id, secondAdministrator.IdentityName, "security-role-tests");
var predefined = await roleCatalog.ListAsync(100);
var builtin = predefined.Where(item => item.IsSystem).ToArray();

if (builtin.Length != 3 ||
    !builtin.Select(item => item.Name).OrderBy(item => item)
        .ToHashSet(StringComparer.Ordinal)
        .SetEquals(new HashSet<string>(StringComparer.Ordinal)
        {
            "Admin", "Engineer", "Operator"
        }) ||
    ProductPermissionCatalog.Definitions.Count != WellSitePermissions.All.Count ||
    ProductPermissionCatalog.Definitions.Select(item => item.Key)
        .ToHashSet(StringComparer.Ordinal)
        .SetEquals(WellSitePermissions.All) == false)
{
    throw new InvalidOperationException("Default role or product permission catalog mismatch.");
}

foreach (var systemRole in builtin)
{
    var defaultKind = Enum.Parse<ApplicationRole>(systemRole.Name);
    foreach (var permission in WellSitePermissions.All)
    {
        if (RolePermissionMap.HasPermission([defaultKind], permission) !=
            systemRole.Permissions.Contains(permission, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("System role seed drifted from the current permission map.");
        }
    }

    try
    {
        await roleCatalog.UpdateAsync(
            systemRole.Id, systemRole.Revision,
            new RoleMutationCommand("Altered", "", [], true), actor);
        throw new InvalidOperationException("Protected system role was editable.");
    }
    catch (WellSiteAutoPilotException exception)
        when (exception.Code == "SECURITY_SYSTEM_ROLE_IMMUTABLE")
    {
    }
}

foreach (var bad in new[]
{
    new RoleMutationCommand("Admin", "", [], true),
    new RoleMutationCommand("A", "", [], true),
    new RoleMutationCommand("Field Lead", "", ["random.unenforced.permission"], true)
})
{
    try
    {
        await roleCatalog.CreateAsync(bad, actor);
        throw new InvalidOperationException("Invalid customer role definition was allowed.");
    }
    catch (WellSiteAutoPilotException exception)
        when (exception.Kind == FailureKind.Validation)
    {
    }
}

try
{
    await roleCatalog.CreateAsync(
        new RoleMutationCommand("Role Test", "", [], true),
        SecurityActorContext.Bootstrap);
    throw new InvalidOperationException("Unauthenticated security actor authored a role.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_ACTOR_REQUIRED")
{
}

try
{
    await roleCatalog.CreateAsync(
        new RoleMutationCommand("Unauthorized Operator Role", "", [], true),
        new SecurityActorContext(operatorUser.Id, operatorUser.IdentityName,
            "unauthorized-role-author"));
    throw new InvalidOperationException("Non-admin author was allowed to create roles.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_ROLE_MANAGE_FORBIDDEN")
{
}

var draftedRole = await roleCatalog.CreateAsync(
    new RoleMutationCommand("Field Supervisor", "Operations lead",
        [WellSitePermissions.AssetsRead, WellSitePermissions.ExecutionsRead], true),
    actor);

if (draftedRole.IsSystem || !draftedRole.IsActive ||
    draftedRole.Revision != 1 ||
    draftedRole.Permissions.Count != 2 ||
    !draftedRole.Permissions.Contains(WellSitePermissions.ExecutionsRead) ||
    (await service.GetRequiredAsync(operatorUser.Id)).Roles.Count != 1 ||
    !RolePermissionMap.HasPermission(
        [ApplicationRole.Admin], WellSitePermissions.SecurityManage))
{
    throw new InvalidOperationException("Role creation changed effective legacy access.");
}

await using (var duplicate = new WellSiteAutoPilotDbContext(options))
{
    var duplicateCatalog = new RoleCatalogService(
        new RoleCatalogRepository(duplicate), new UserAccessRepository(duplicate), clock);
    try
    {
        await duplicateCatalog.CreateAsync(
            new RoleMutationCommand("field supervisor", "Different case", [], true), actor);
        throw new InvalidOperationException("Case-insensitive role name uniqueness was bypassed.");
    }
    catch (WellSiteAutoPilotException exception)
        when (exception.Code == "SECURITY_ROLE_CONFLICT")
    {
    }
}

var updatedRole = await roleCatalog.UpdateAsync(
    draftedRole.Id, draftedRole.Revision,
    new RoleMutationCommand("Field Supervisor", "Revised",
        [WellSitePermissions.AssetsRead], false),
    actor);

if (updatedRole.IsActive || updatedRole.Revision != 2 ||
    updatedRole.Permissions.Count != 1 ||
    updatedRole.Description != "Revised")
{
    throw new InvalidOperationException("Versioned role permission replacement failed.");
}

try
{
    await roleCatalog.UpdateAsync(
        draftedRole.Id, draftedRole.Revision,
        new RoleMutationCommand("Field Supervisor", "Stale", [], true), actor);
    throw new InvalidOperationException("Stale role update was accepted.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_ROLE_CONFLICT")
{
}

var roleAudit = await dbContext.AuditEvents.AsNoTracking()
    .Where(item => item.TargetId == draftedRole.Id.ToString("D") &&
        item.TargetType == "security-role")
    .OrderBy(item => item.OccurredAtUtc)
    .ToArrayAsync();

if (roleAudit.Length != 2 ||
    roleAudit.Any(item => item.ActorUserId != actor.UserId ||
        item.CorrelationId != actor.CorrelationId) ||
    !roleAudit.Any(item => item.Action == "security.role.created") ||
    !roleAudit.Any(item => item.Action == "security.role.updated"))
{
    throw new InvalidOperationException("Role authoring audit did not round-trip.");
}

// Concurrent revisions can have only one winning write.
var competingRole = await roleCatalog.CreateAsync(
    new RoleMutationCommand("Shift Leader", "", [WellSitePermissions.AssetsRead], true),
    actor);
await using (var first = new WellSiteAutoPilotDbContext(options))
await using (var second = new WellSiteAutoPilotDbContext(options))
{
    var writerA = new RoleCatalogService(
        new RoleCatalogRepository(first), new UserAccessRepository(first), clock);
    var writerB = new RoleCatalogService(
        new RoleCatalogRepository(second), new UserAccessRepository(second), clock);
    var attempts = await Task.WhenAll(
        TryRoleWriteAsync(writerA, competingRole.Id, competingRole.Revision,
            new RoleMutationCommand("Shift Leader A", "", [], true), actor),
        TryRoleWriteAsync(writerB, competingRole.Id, competingRole.Revision,
            new RoleMutationCommand("Shift Leader B", "", [], true), actor));

    if (attempts.Count(item => item) != 1 ||
        (await roleCatalog.GetRequiredAsync(competingRole.Id)).Revision != 2)
    {
        throw new InvalidOperationException("Competing role updates did not have one canonical winner.");
    }
}

// The new grant evaluator must never combine the roles from one assignment
// with the Asset scope from another. This invariant is security-critical.
var grantUserId = Guid.NewGuid();
var wellA = Guid.NewGuid();
var wellB = Guid.NewGuid();
var engineerRole = new RoleDefinition(
    Guid.NewGuid(), "test-engineer", "Test Engineer", "", false, true, 1,
    clock.GetUtcNow(), clock.GetUtcNow(),
    [WellSitePermissions.AssetsRead, WellSitePermissions.AssetsManage]);
var operatorRole = new RoleDefinition(
    Guid.NewGuid(), "test-operator", "Test Operator", "", false, true, 1,
    clock.GetUtcNow(), clock.GetUtcNow(),
    [WellSitePermissions.AssetsRead]);
var engineerGrant = new AccessGrant(
    Guid.NewGuid(), AccessGrantSubjectKind.User, grantUserId, null,
    engineerRole.Id, AccessGrantScopeKind.ExactAssets, [wellA], true, 1);
var operatorGrant = new AccessGrant(
    Guid.NewGuid(), AccessGrantSubjectKind.User, grantUserId, null,
    operatorRole.Id, AccessGrantScopeKind.ExactAssets, [wellB], true, 1);

var applicableRoles = new[] { engineerRole, operatorRole };
var grants = new[] { engineerGrant, operatorGrant };
IReadOnlySet<Guid> activeWellIds = new HashSet<Guid> { wellA, wellB };

var engineerOnA = ScopedGrantEvaluator.Authorize(
    grantUserId, true, WellSitePermissions.AssetsManage, wellA,
    activeWellIds, grants, applicableRoles);
var engineerOnB = ScopedGrantEvaluator.Authorize(
    grantUserId, true, WellSitePermissions.AssetsManage, wellB,
    activeWellIds, grants, applicableRoles);
var readOnB = ScopedGrantEvaluator.Authorize(
    grantUserId, true, WellSitePermissions.AssetsRead, wellB,
    activeWellIds, grants, applicableRoles);

if (!engineerOnA.Allowed || engineerOnA.GrantedByAssignmentId != engineerGrant.Id ||
    engineerOnB.Allowed || !readOnB.Allowed ||
    readOnB.GrantedByAssignmentId != operatorGrant.Id)
{
    throw new InvalidOperationException("Role and Asset scopes leaked between distinct grants.");
}

foreach (var access in new[]
{
    ScopedGrantEvaluator.Authorize(
        grantUserId, false, WellSitePermissions.AssetsRead, wellA,
        activeWellIds, grants, applicableRoles),
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsRead, wellB,
        new HashSet<Guid> { wellA }, grants, applicableRoles),
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, "unsupported.fake.permission", wellA,
        activeWellIds, grants, applicableRoles),
    ScopedGrantEvaluator.Authorize(
        Guid.NewGuid(), true, WellSitePermissions.AssetsRead, wellA,
        activeWellIds, grants, applicableRoles),
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.SecurityManage, null,
        activeWellIds, grants, applicableRoles),
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsRead, null,
        activeWellIds, grants, applicableRoles),
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsRead, wellA,
        activeWellIds, [engineerGrant with { IsActive = false }], applicableRoles),
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsRead, wellA,
        activeWellIds, [engineerGrant], [engineerRole with { IsActive = false }]),
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsRead, wellA,
        activeWellIds, [engineerGrant with { ScopeKind = AccessGrantScopeKind.ExactAssets,
            AssetIds = [Guid.Empty, wellA] }], applicableRoles),
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsRead, wellA,
        activeWellIds, [engineerGrant with { SubjectKind = AccessGrantSubjectKind.WindowsGroup,
            UserId = null, GroupSid = "S-1-5-21-1234" }], applicableRoles)
})
{
    if (access.Allowed)
    {
        throw new InvalidOperationException("Invalid or unverified security grant authorized access.");
    }
}

var globalRole = new RoleDefinition(
    Guid.NewGuid(), "test-security-role", "Test Security", "", false, true, 1,
    clock.GetUtcNow(), clock.GetUtcNow(), [WellSitePermissions.SecurityManage]);
var globalGrant = new AccessGrant(
    Guid.NewGuid(), AccessGrantSubjectKind.User, grantUserId, null,
    globalRole.Id, AccessGrantScopeKind.DeploymentWide, [], true, 1);

if (!ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.SecurityManage, null,
        activeWellIds, [globalGrant], [globalRole]).Allowed ||
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.SecurityManage, null,
        activeWellIds, [globalGrant with { ScopeKind = AccessGrantScopeKind.ExactAssets,
            AssetIds = [wellA] }], [globalRole]).Allowed ||
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.SecurityManage, null,
        activeWellIds, [globalGrant with { AssetIds = [wellA] }], [globalRole]).Allowed)
{
    throw new InvalidOperationException("Deployment-wide permission scope validation failed.");
}

// A mixed-purpose Engineer role must not have its Asset permissions leaked
// by a separate deployment-wide grant for non-Asset catalog permissions.
var mixedRole = new RoleDefinition(
    Guid.NewGuid(), "test-mixed-role", "Mixed Role", "", false, true, 1,
    clock.GetUtcNow(), clock.GetUtcNow(),
    [WellSitePermissions.LogicRead, WellSitePermissions.AssetsManage]);
var globalLogicGrant = engineerGrant with
{
    Id = Guid.NewGuid(),
    RoleId = mixedRole.Id,
    ScopeKind = AccessGrantScopeKind.DeploymentWide,
    AssetIds = []
};
var exactMixedGrant = globalLogicGrant with
{
    Id = Guid.NewGuid(),
    ScopeKind = AccessGrantScopeKind.ExactAssets,
    AssetIds = [wellA]
};
var explicitAllAssets = globalLogicGrant with
{
    Id = Guid.NewGuid(),
    ScopeKind = AccessGrantScopeKind.AllAssets
};

if (!ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.LogicRead, null, activeWellIds,
        [globalLogicGrant], [mixedRole]).Allowed ||
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsManage, wellB, activeWellIds,
        [globalLogicGrant, exactMixedGrant], [mixedRole]).Allowed ||
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.LogicRead, null, activeWellIds,
        [exactMixedGrant], [mixedRole]).Allowed ||
    !ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsManage, wellB, activeWellIds,
        [explicitAllAssets], [mixedRole]).Allowed ||
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.LogicRead, null, activeWellIds,
        [explicitAllAssets], [mixedRole]).Allowed ||
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsManage, wellB, activeWellIds,
        [explicitAllAssets with { AssetIds = [wellA] }], [mixedRole]).Allowed)
{
    throw new InvalidOperationException(
        "Deployment-wide and explicit all-Asset grants leaked permissions across scope classes.");
}

var grantCatalogRole = await roleCatalog.CreateAsync(
    new RoleMutationCommand("Scoped Assignment Role", "Grant regression role",
        [WellSitePermissions.AssetsRead], true), actor);

// The first persisted grants are additive and audit-only. Existing user's
// compiled roles and Asset scope remain unchanged until evaluator cutover.
var accessGrantService = new AccessGrantAdministrationService(
    new AccessGrantRepository(dbContext),
    repository,
    new RoleCatalogRepository(dbContext),
    clock);
var assignmentActor = new SecurityActorContext(
    secondAdministrator.Id, secondAdministrator.IdentityName, "grant-test");

var draftAccessBefore = await service.GetRequiredAsync(operatorUser.Id);
var persisted = await accessGrantService.CreateForUserAsync(
    operatorUser.Id,
    new CreateAccessGrantCommand(
        grantCatalogRole.Id, AccessGrantScopeKind.ExactAssets, [asset.Id]),
    assignmentActor);

// A custom role already referenced by an active entitlement must not
// silently gain new permissions. Retire the assignment first.
try
{
    await roleCatalog.UpdateAsync(
        grantCatalogRole.Id, grantCatalogRole.Revision,
        new RoleMutationCommand(
            grantCatalogRole.Name, grantCatalogRole.Description,
            [WellSitePermissions.AssetsRead, WellSitePermissions.AssetsManage], true),
        actor);
    throw new InvalidOperationException(
        "Editing an assigned role silently expanded its effective permissions.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_ROLE_ASSIGNED")
{
}

if (!persisted.IsActive || persisted.Revision != 1 ||
    persisted.SubjectKind != AccessGrantSubjectKind.User ||
    persisted.UserId != operatorUser.Id ||
    !persisted.AssetIds.SequenceEqual([asset.Id]))
{
    throw new InvalidOperationException("Persisted direct scoped grant did not round-trip.");
}

await using (var reloadedContext = new WellSiteAutoPilotDbContext(options))
{
    var reloadedService = new AccessGrantAdministrationService(
        new AccessGrantRepository(reloadedContext),
        new UserAccessRepository(reloadedContext),
        new RoleCatalogRepository(reloadedContext),
        clock);
    var loaded = await reloadedService.ListForUserAsync(
        operatorUser.Id, assignmentActor);
    if (loaded.Count != 1 || loaded.Single().Id != persisted.Id ||
        loaded.Single().AssetIds.Single() != asset.Id)
    {
        throw new InvalidOperationException("Durable scoped grant was not visible after reload.");
    }
}

var draftAccessAfter = await service.GetRequiredAsync(operatorUser.Id);
if (!draftAccessAfter.Roles.SequenceEqual(draftAccessBefore.Roles) ||
    !draftAccessAfter.AssetScopeIds.SequenceEqual(draftAccessBefore.AssetScopeIds))
{
    throw new InvalidOperationException("New grant unexpectedly changed effective legacy access.");
}

// A migration preview must reproduce the complete legacy role × Asset
// cross-product, with separate DeploymentWide and resource grants. This is
// deliberately read-only and not sufficient to approve production cutover.
IReadOnlySet<Guid> knownMigrationAssets = new HashSet<Guid>
{
    asset.Id, otherAsset.Id
};
var operatorMigration = LegacyGrantMigrationPlanner.PreviewForKnownAssets(
    draftAccessBefore, builtin, knownMigrationAssets);
if (operatorMigration.Count != 2 ||
    operatorMigration.Count(item =>
        item.ScopeKind == AccessGrantScopeKind.DeploymentWide) != 1 ||
    operatorMigration.Count(item =>
        item.ScopeKind == AccessGrantScopeKind.ExactAssets &&
        item.AssetIds.SequenceEqual([asset.Id])) != 1)
{
    throw new InvalidOperationException(
        "Legacy Operator migration did not preserve mixed scope semantics.");
}
var adminMigration = LegacyGrantMigrationPlanner.PreviewForKnownAssets(
    await service.GetRequiredAsync(secondAdministrator.Id),
    builtin, knownMigrationAssets);
if (adminMigration.Count != 2 ||
    !adminMigration.Any(item => item.ScopeKind == AccessGrantScopeKind.AllAssets) ||
    !adminMigration.Any(item => item.ScopeKind == AccessGrantScopeKind.DeploymentWide))
{
    throw new InvalidOperationException(
        "Legacy administrator migration lost AllAssets or deployment permissions.");
}
var combinedLegacy = draftAccessBefore with
{
    Roles = [ApplicationRole.Admin, ApplicationRole.Operator]
};
var combinedMigration = LegacyGrantMigrationPlanner.PreviewForKnownAssets(
    combinedLegacy, builtin, knownMigrationAssets);
var operatorSystemRole = builtin.Single(item => item.Name == "Operator");
var adminSystemRole = builtin.Single(item => item.Name == "Admin");
if (!combinedMigration.Any(item =>
        item.RoleId == adminSystemRole.Id &&
        item.ScopeKind == AccessGrantScopeKind.AllAssets) ||
    !combinedMigration.Any(item =>
        item.RoleId == operatorSystemRole.Id &&
        item.ScopeKind == AccessGrantScopeKind.ExactAssets &&
        item.AssetIds.SequenceEqual([asset.Id])) ||
    combinedMigration.Any(item =>
        item.RoleId == operatorSystemRole.Id &&
        item.ScopeKind == AccessGrantScopeKind.AllAssets))
{
    throw new InvalidOperationException(
        "Removing Admin could leave another role with undeserved AllAssets rights.");
}

try
{
    _ = LegacyGrantMigrationPlanner.PreviewForKnownAssets(
        draftAccessBefore, builtin, new HashSet<Guid> { otherAsset.Id });
    throw new InvalidOperationException("Migration silently dropped the user's Asset scope.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_LEGACY_MIGRATION_PARITY_FAILED")
{
}
var alteredBuiltin = builtin.Select(role =>
    role.Name == "Operator"
        ? role with { Permissions = [WellSitePermissions.AssetsManage] }
        : role).ToArray();
try
{
    _ = LegacyGrantMigrationPlanner.PreviewForKnownAssets(
        draftAccessBefore, alteredBuiltin, knownMigrationAssets);
    throw new InvalidOperationException("Migration accepted system-role permission drift.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_LEGACY_MIGRATION_PARITY_FAILED")
{
}

// Compare actual persisted direct-user grants against current production
// roles for one explicitly selected active Asset. This is diagnostic only;
// it cannot certify whole-deployment authorization equivalence.
var inspector = new ScopedAccessInspectionService(
    accessGrantService, repository, new AccessGrantRepository(dbContext),
    new RoleCatalogRepository(dbContext), new AssetRepository(dbContext));
// Capture the complete active Asset inventory, built-in/custom roles,
 // recorded grants and user scope under ONE PostgreSQL repeatable-read
 // transaction. This read-only preflight is not an authorization cutover.
var migrationPreflight = new LegacyMigrationPreflightService(
    accessGrantService, new LegacyMigrationSnapshotRepository(dbContext));
var beforePreflightAudits = await dbContext.AuditEvents.CountAsync();
var preflightReport = await migrationPreflight.InspectAsync(
    operatorUser.Id, assignmentActor);
var afterPreflightAudits = await dbContext.AuditEvents.CountAsync();
if (!preflightReport.IsDiagnosticOnly ||
    preflightReport.RecordedGrantsMatchLegacy ||
    preflightReport.ExpectedLegacyGrantCount != 2 ||
    preflightReport.RecordedGrantCount != 1 ||
    preflightReport.ExpansionCount != 0 ||
    preflightReport.ReductionCount < 2 ||
    preflightReport.ActiveAssetCount < 2 ||
    afterPreflightAudits != beforePreflightAudits)
{
    throw new InvalidOperationException(
        "Migration preflight did not capture safe read-only legacy parity.");
}
var missingGlobal = preflightReport.DifferenceSamples
    .Where(item => item.Permission == WellSitePermissions.LogicRead).ToArray();
if (missingGlobal.Length != 1 ||
    missingGlobal.Single().AssetId is not null ||
    !missingGlobal.Single().LegacyAllowed ||
    missingGlobal.Single().RecordedGrantAllowed)
{
    throw new InvalidOperationException(
        "Deployment-wide parity discrepancy was repeated per Asset.");
}
try
{
    await migrationPreflight.InspectAsync(
        operatorUser.Id,
        new SecurityActorContext(
            administrator.Id, administrator.IdentityName, "demoted-preflight"));
    throw new InvalidOperationException(
        "A demoted administrator requested migration evidence.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_ADMIN_REQUIRED")
{
}
try
{
    await migrationPreflight.InspectAsync(Guid.NewGuid(), assignmentActor);
    throw new InvalidOperationException(
        "An unknown user was accepted by the migration preflight.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_USER_NOT_ACTIVE")
{
}

// A lexicographically earlier unauthorized resource catches regressions
// that page all Assets first and only then check permissions.
var unassignedEarlyAsset = await assetService.CreateAssetAsync(
    new CreateAssetCommand(
        assetType.Id, "AAA Unassigned Preview Asset", null, "{}"));

// Proposed Asset-resource previews must filter in SQL *before* paging.
// Another visible Asset, or a permission held by another role, must never
// fill the first page or leak into the grant's effective Resource scope.
var proposedAccess = new ProposedAssetAccessService(
    accessGrantService, repository, new ProposedAssetAccessRepository(dbContext));
var filteredPage = await proposedAccess.ListAsync(
    operatorUser.Id, WellSitePermissions.AssetsRead, 0, 1, assignmentActor);
var beyondPage = await proposedAccess.ListAsync(
    operatorUser.Id, WellSitePermissions.AssetsRead, 1, 1, assignmentActor);
var noManage = await proposedAccess.ListAsync(
    operatorUser.Id, WellSitePermissions.AssetsManage, 0, 100, assignmentActor);
if (!filteredPage.IsDiagnosticOnly ||
    filteredPage.Assets.Count != 1 ||
    filteredPage.Assets.Single().Id != asset.Id ||
    beyondPage.Assets.Count != 0 ||
    noManage.Assets.Count != 0 ||
    filteredPage.Assets.Any(item => item.Id == unassignedEarlyAsset.Id))
{
    throw new InvalidOperationException(
        "Proposed SQL resource filtering leaked scope or applied paging too early.");
}

try
{
    await proposedAccess.ListAsync(operatorUser.Id,
        WellSitePermissions.SecurityManage, 0, 100, assignmentActor);
    throw new InvalidOperationException(
        "A deployment-wide permission was accepted as resource scope.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_PREVIEW_QUERY_INVALID")
{
}
try
{
    await proposedAccess.ListAsync(operatorUser.Id,
        WellSitePermissions.AssetsRead, 0, 100,
        new SecurityActorContext(
            administrator.Id, administrator.IdentityName, "demoted-preview"));
    throw new InvalidOperationException(
        "A demoted administrator read proposed Asset access.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_ADMIN_REQUIRED")
{
}

try
{
    await proposedAccess.ListAsync(
        Guid.NewGuid(), WellSitePermissions.AssetsRead, 0, 100, assignmentActor);
    throw new InvalidOperationException(
        "An unknown user returned a valid scoped access preview.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_USER_NOT_ACTIVE")
{
}

// Inactive resources must never appear in the SQL projection, even
// before the grant has been explicitly retired. Roll back the test change.
await using (var inactiveAssetTransaction =
    await dbContext.Database.BeginTransactionAsync())
{
    await dbContext.Assets
        .Where(item => item.Id == asset.Id)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.IsActive, false));
    var inactivePage = await proposedAccess.ListAsync(
        operatorUser.Id, WellSitePermissions.AssetsRead, 0, 100, assignmentActor);
    if (inactivePage.Assets.Count != 0)
    {
        throw new InvalidOperationException(
            "Proposed SQL projection included an inactive Asset.");
    }
    await inactiveAssetTransaction.RollbackAsync();
}
var restoredPage = await proposedAccess.ListAsync(
    operatorUser.Id, WellSitePermissions.AssetsRead, 0, 100, assignmentActor);
if (restoredPage.Assets.Count != 1 ||
    restoredPage.Assets.Single().Id != asset.Id)
{
    throw new InvalidOperationException(
        "Rolled-back Asset state was not reflected in proposed authorization.");
}

// An explicit AllAssets grant authorizes resources only for its own user
// and only for the role's resource permissions.
var allAssetsDraft = await accessGrantService.CreateForUserAsync(
    administrator.Id,
    new CreateAccessGrantCommand(
        grantCatalogRole.Id, AccessGrantScopeKind.AllAssets, []),
    assignmentActor);
var allAssetsPreview = await proposedAccess.ListAsync(
    administrator.Id, WellSitePermissions.AssetsRead, 0, 500, assignmentActor);
var otherUserPreview = await proposedAccess.ListAsync(
    operatorUser.Id, WellSitePermissions.AssetsRead, 0, 500, assignmentActor);
if (!allAssetsDraft.IsActive ||
    !allAssetsPreview.Assets.Any(item => item.Id == asset.Id) ||
    !allAssetsPreview.Assets.Any(item => item.Id == otherAsset.Id) ||
    otherUserPreview.Assets.Count != 1 ||
    otherUserPreview.Assets.Single().Id != asset.Id)
{
    throw new InvalidOperationException(
        "AllAssets scope was not explicit or leaked between users.");
}

var matchingAsset = await inspector.InspectAsync(
    operatorUser.Id, asset.Id, assignmentActor);
var otherAssetInspection = await inspector.InspectAsync(
    operatorUser.Id, otherAsset.Id, assignmentActor);
var assetRead = matchingAsset.Permissions.Single(item =>
    item.Permission == WellSitePermissions.AssetsRead);
var legacyRecommendation = matchingAsset.Permissions.Single(item =>
    item.Permission == WellSitePermissions.RecommendationsRead);
var otherAssetRead = otherAssetInspection.Permissions.Single(item =>
    item.Permission == WellSitePermissions.AssetsRead);
if (!matchingAsset.IsDiagnosticOnly || !assetRead.LegacyAllowed ||
    !assetRead.ProposedAllowed || assetRead.ProposedGrantId != persisted.Id ||
    !legacyRecommendation.LegacyAllowed || legacyRecommendation.ProposedAllowed ||
    otherAssetRead.LegacyAllowed || otherAssetRead.ProposedAllowed ||
    matchingAsset.ReductionCount == 0 || matchingAsset.WideningCount != 0)
{
    throw new InvalidOperationException(
        "Persisted access-inspector comparison failed or widened cross-Asset access.");
}

var globalInspection = await inspector.InspectAsync(
    operatorUser.Id, null, assignmentActor);
if (globalInspection.AssetId is not null ||
    globalInspection.Permissions.Any(item => item.ScopeKind != "DeploymentWide") ||
    globalInspection.WideningCount != 0)
{
    throw new InvalidOperationException(
        "Global-only inspection incorrectly compared resource permissions.");
}
try
{
    await inspector.InspectAsync(operatorUser.Id, asset.Id,
        new SecurityActorContext(
            administrator.Id, administrator.IdentityName, "demoted-inspector"));
    throw new InvalidOperationException("Demoted admin inspected scoped access.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_ADMIN_REQUIRED")
{
}
try
{
    await inspector.InspectAsync(operatorUser.Id, Guid.NewGuid(), assignmentActor);
    throw new InvalidOperationException("Nonexistent Asset was inspected.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_INSPECTION_ASSET_INACTIVE")
{
}

var syntheticGrantWidening = ScopedAccessComparisonEvaluator.Compare(
    operatorUser with { Roles = [], AssetScopeIds = [] },
    wellA, "Synthetic Well A", activeWellIds, [engineerGrant], [engineerRole]);
// The grant belongs to grantUserId, not this Operator. Role and Asset
// coincidence must never authorize another user.
if (syntheticGrantWidening.WideningCount != 0 ||
    syntheticGrantWidening.Permissions.Any(item => item.ProposedAllowed))
{
    throw new InvalidOperationException("Cross-user grant leaked through inspector.");
}
var syntheticUser = operatorUser with
{
    Id = grantUserId,
    Roles = [],
    AssetScopeIds = []
};
var widening = ScopedAccessComparisonEvaluator.Compare(
    syntheticUser, wellA, "Synthetic Well A",
    activeWellIds, [engineerGrant], [engineerRole]);
if (widening.WideningCount == 0 ||
    widening.Permissions.Single(item =>
        item.Permission == WellSitePermissions.AssetsManage).ProposedGrantId !=
        engineerGrant.Id)
{
    throw new InvalidOperationException(
        "Inspector failed to detect a proposed privilege expansion.");
}

foreach (var invalidGrant in new[]
{
    new CreateAccessGrantCommand(grantCatalogRole.Id, AccessGrantScopeKind.ExactAssets, []),
    new CreateAccessGrantCommand(grantCatalogRole.Id, AccessGrantScopeKind.ExactAssets,
        [otherAsset.Id, otherAsset.Id]),
    new CreateAccessGrantCommand(grantCatalogRole.Id, AccessGrantScopeKind.AllAssets,
        [asset.Id]),
    new CreateAccessGrantCommand(grantCatalogRole.Id, AccessGrantScopeKind.DeploymentWide,
        [asset.Id]),
    new CreateAccessGrantCommand(Guid.NewGuid(), AccessGrantScopeKind.ExactAssets,
        [asset.Id])
})
{
    try
    {
        await accessGrantService.CreateForUserAsync(
            operatorUser.Id, invalidGrant, assignmentActor);
        throw new InvalidOperationException("Invalid scoped grant was accepted.");
    }
    catch (WellSiteAutoPilotException exception)
        when (exception.Kind == FailureKind.Validation)
    {
    }
}

try
{
    await accessGrantService.CreateForUserAsync(
        secondAdministrator.Id,
        new CreateAccessGrantCommand(
            grantCatalogRole.Id, AccessGrantScopeKind.ExactAssets, [asset.Id]),
        assignmentActor);
    throw new InvalidOperationException("Administrator assigned their own grant.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_SELF_ACCESS_CHANGE_FORBIDDEN")
{
}

// The original bootstrap admin was deliberately demoted earlier in this
// suite. Historical administrator identity must not bypass current access.
try
{
    await accessGrantService.CreateForUserAsync(
        operatorUser.Id,
        new CreateAccessGrantCommand(
            grantCatalogRole.Id, AccessGrantScopeKind.ExactAssets, [asset.Id]),
        new SecurityActorContext(
            administrator.Id, administrator.IdentityName, "demoted-admin"));
    throw new InvalidOperationException("Demoted administrator created security grant.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_ADMIN_REQUIRED")
{
}

try
{
    await accessGrantService.CreateForUserAsync(
        operatorUser.Id,
        new CreateAccessGrantCommand(
            grantCatalogRole.Id, AccessGrantScopeKind.ExactAssets, [asset.Id]),
        new SecurityActorContext(
            operatorUser.Id, operatorUser.IdentityName, "not-admin"));
    throw new InvalidOperationException("Non-admin created security grant.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_ADMIN_REQUIRED")
{
}

try
{
    await accessGrantService.CreateForUserAsync(
        operatorUser.Id,
        new CreateAccessGrantCommand(
            grantCatalogRole.Id, AccessGrantScopeKind.ExactAssets, [asset.Id]),
        assignmentActor);
    throw new InvalidOperationException("Duplicate active grant was accepted.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_GRANT_CONFLICT")
{
}

// A demoted historical administrator must not be able to retire grants.
try
{
    await accessGrantService.DeactivateForUserAsync(
        operatorUser.Id, persisted.Id, persisted.Revision,
        new SecurityActorContext(
            administrator.Id, administrator.IdentityName, "retire-demoted-admin"));
    throw new InvalidOperationException("Demoted administrator retired a grant.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_ADMIN_REQUIRED")
{
}
try
{
    await accessGrantService.DeactivateForUserAsync(
        secondAdministrator.Id, persisted.Id, persisted.Revision, assignmentActor);
    throw new InvalidOperationException("Administrator retired their own grant.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_SELF_ACCESS_CHANGE_FORBIDDEN")
{
}

// Deactivation is an explicit, revision-fenced, audited draft-only mutation.
var deactivated = await accessGrantService.DeactivateForUserAsync(
    operatorUser.Id, persisted.Id, persisted.Revision, assignmentActor);
if (deactivated.IsActive || deactivated.Revision != 2 ||
    deactivated.AssetIds.Single() != asset.Id)
{
    throw new InvalidOperationException("Versioned grant deactivation failed.");
}
try
{
    await accessGrantService.DeactivateForUserAsync(
        operatorUser.Id, persisted.Id, persisted.Revision, assignmentActor);
    throw new InvalidOperationException("Stale deactivation was accepted.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_GRANT_CONFLICT")
{
}
await using (var verification = new WellSiteAutoPilotDbContext(options))
{
    var inactive = await new AccessGrantRepository(verification)
        .ListForUserAsync(operatorUser.Id);
    if (inactive.Count != 1 || inactive.Single().IsActive ||
        inactive.Single().Revision != 2)
    {
        throw new InvalidOperationException("Inactive grant did not round-trip.");
    }
    var audit = await verification.AuditEvents.AsNoTracking()
        .SingleOrDefaultAsync(item => item.Action == "security.grant.deactivated" &&
            item.TargetId == persisted.Id.ToString("D"));
    if (audit is null || audit.ActorUserId != secondAdministrator.Id ||
        audit.CorrelationId != "grant-test")
    {
        throw new InvalidOperationException("Deactivation audit missing.");
    }
}

var grantAudit = await dbContext.AuditEvents.AsNoTracking()
    .Where(item => item.Action == "security.grant.created" &&
        item.TargetId == persisted.Id.ToString("D"))
    .SingleOrDefaultAsync();
if (grantAudit is null ||
    grantAudit.ActorUserId != secondAdministrator.Id ||
    grantAudit.CorrelationId != "grant-test")
{
    throw new InvalidOperationException("New scoped grant missing audited actor/provenance.");
}

Console.WriteLine(
    "Stable Windows identity, role authoring, optimistic concurrency, RBAC, Asset scope, audit, and last-Admin invariants passed.");
return 0;


static async Task<bool> TryRoleWriteAsync(
    RoleCatalogService service,
    Guid roleId,
    long revision,
    RoleMutationCommand command,
    SecurityActorContext actor)
{
    try
    {
        await service.UpdateAsync(roleId, revision, command, actor);
        return true;
    }
    catch (WellSiteAutoPilotException exception)
        when (exception.Code == "SECURITY_ROLE_CONFLICT")
    {
        return false;
    }
}

file sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration)
    {
        _utcNow = _utcNow.Add(duration);
    }
}
