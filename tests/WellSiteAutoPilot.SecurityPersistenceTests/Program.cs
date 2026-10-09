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
    when (exception.Code == "SECURITY_LAST_ADMIN_REQUIRED")
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

Console.WriteLine(
    "Stable Windows identity, one-time bootstrap Admin, RBAC, exact Asset scope isolation, durable audit, and last-Admin invariants passed.");
return 0;

file sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan duration)
    {
        _utcNow = _utcNow.Add(duration);
    }
}
