using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
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

var bootstrapIdentity =
    UserAccessService.NormalizeIdentity(@"FIELD\wsa-admin");
var bootstrap = new HashSet<string>(
    [bootstrapIdentity],
    StringComparer.Ordinal);

var administrator = await service.ResolveAuthenticatedAsync(
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
    @"field\WSA-ADMIN",
    "WSA Administrator",
    bootstrap);

if (sameAdministrator.Id != administrator.Id ||
    sameAdministrator.LastSeenAtUtc <= administrator.LastSeenAtUtc)
{
    throw new InvalidOperationException(
        "Windows identity normalization created a duplicate user or did not update last-seen state.");
}

var operatorUser = await service.ResolveAuthenticatedAsync(
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
    [asset.Id]);

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
        [Guid.NewGuid()]);

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
        []);

    throw new InvalidOperationException(
        "The final Admin role was removed.");
}
catch (WellSiteAutoPilotException exception)
    when (exception.Code == "SECURITY_LAST_ADMIN_REQUIRED")
{
}

var secondAdministrator = await service.ResolveAuthenticatedAsync(
    @"FIELD\wsa-admin-two",
    "WSA Administrator Two",
    new HashSet<string>(
        [UserAccessService.NormalizeIdentity(@"FIELD\wsa-admin-two")],
        StringComparer.Ordinal));

await service.ReplaceAccessAsync(
    administrator.Id,
    [ApplicationRole.Engineer],
    [asset.Id]);

var updatedAdministrator = await service.GetRequiredAsync(
    administrator.Id);

if (updatedAdministrator.Roles.Contains(ApplicationRole.Admin) ||
    !updatedAdministrator.Roles.Contains(ApplicationRole.Engineer) ||
    secondAdministrator.Roles.Count != 1 ||
    !secondAdministrator.Roles.Contains(ApplicationRole.Admin))
{
    throw new InvalidOperationException(
        "Admin replacement invariant did not preserve at least one active administrator.");
}

var users = await service.ListAsync(100);
if (users.Count < 3 ||
    users.Select(item => item.NormalizedIdentityName).Distinct().Count() !=
    users.Count)
{
    throw new InvalidOperationException(
        "Security user persistence returned duplicate normalized identities.");
}

Console.WriteLine(
    "Security identity, bootstrap Admin, RBAC, Asset scope, and last-Admin invariants passed.");
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
