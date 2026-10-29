using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;

SecurityActorGroupGuardChecks.Run();
await GroupDirectorySearchChecks.RunAsync();
var clock = TimeProvider.System;

// The new grant evaluator must never combine the roles from one assignment
// with the Asset scope from another. This invariant is security-critical.
var grantUserId = Guid.NewGuid();
try { _ = new AccessSubject(Guid.Empty); throw new InvalidOperationException("Empty request subject accepted."); }
catch (ArgumentException) { }
try
{
    _ = new AccessSubject(grantUserId, new VerifiedWindowsGroupMembership(Guid.NewGuid(), [], clock.GetUtcNow()));
    throw new InvalidOperationException("Mismatched request group subject accepted.");
}
catch (ArgumentException) { }
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

// The pure evaluator and SQL projection must agree on the maximum number
// of exact Asset bindings. A malformed imported/migrated grant with 501
// active Assets must never authorize a target that happens to be in scope.
var maximumScopeAssetIds = Enumerable.Range(
        0, ScopedGrantEvaluator.MaximumExactAssetsPerGrant - 1)
    .Select(_ => Guid.NewGuid())
    .Append(wellA)
    .ToArray();
var extraScopeAssetId = Guid.NewGuid();
var oversizedScopeAssetIds = maximumScopeAssetIds
    .Append(extraScopeAssetId).ToArray();
IReadOnlySet<Guid> allBoundedActiveAssetIds =
    oversizedScopeAssetIds.ToHashSet();

if (!ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsRead, wellA,
        allBoundedActiveAssetIds,
        [engineerGrant with { AssetIds = maximumScopeAssetIds }],
        [engineerRole]).Allowed ||
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsRead, wellA,
        allBoundedActiveAssetIds,
        [engineerGrant with { AssetIds = oversizedScopeAssetIds }],
        [engineerRole]).Allowed ||
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsRead, wellA,
        activeWellIds,
        [engineerGrant with { AssetIds = [wellA, wellA] }],
        [engineerRole]).Allowed)
{
    throw new InvalidOperationException(
        "Pure grant evaluator failed exact-Asset cardinality/duplicate validation.");
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


// Invalid imported role revisions fail closed for every scope class, while
// an independent valid role/grant remains available.
foreach (var invalidRevision in new long[] { 0, -1 })
{
    var invalidRole = engineerRole with { Revision = invalidRevision };
    foreach (var scoped in new[] { engineerGrant, engineerGrant with
        { ScopeKind = AccessGrantScopeKind.AllAssets, AssetIds = [] } })
        if (ScopedGrantEvaluator.Authorize(grantUserId, true, WellSitePermissions.AssetsRead,
            wellA, activeWellIds, [scoped], [invalidRole]).Allowed)
            throw new InvalidOperationException("An invalid role revision authorized Asset access.");
    if (ScopedGrantEvaluator.Authorize(grantUserId, true, WellSitePermissions.SecurityManage,
        null, activeWellIds, [globalGrant], [globalRole with { Revision = invalidRevision }]).Allowed)
        throw new InvalidOperationException("An invalid role revision authorized global access.");
    if (!ScopedGrantEvaluator.Authorize(grantUserId, true, WellSitePermissions.AssetsRead,
        wellB, activeWellIds, grants, [invalidRole, operatorRole]).Allowed)
        throw new InvalidOperationException("An invalid role disabled an unrelated valid assignment.");
}

// Malformed imported records never become usable grants, even when their
// role contains the requested permission.
foreach (var malformed in new[]
{
    engineerGrant with { Id = Guid.Empty },
    engineerGrant with { Revision = 0 },
    engineerGrant with { UserId = null },
    engineerGrant with { UserId = Guid.NewGuid() },
    engineerGrant with { GroupSid = "S-1-5-21-100" },
    engineerGrant with { SubjectKind = (AccessGrantSubjectKind)99 },
    engineerGrant with { ScopeKind = (AccessGrantScopeKind)99 },
    engineerGrant with { RoleId = Guid.NewGuid() },
    engineerGrant with { AssetIds = [] },
    engineerGrant with { AssetIds = [wellA, Guid.NewGuid()] }
})
{
    var denied = ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsRead, wellA,
        activeWellIds, [malformed], applicableRoles);
    if (denied.Allowed || denied.GrantedByAssignmentId is not null ||
        denied.GrantedBySubjectKind is not null)
    {
        throw new InvalidOperationException("Malformed grant authorized or claimed grant provenance.");
    }
}

if (ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsRead, wellA,
        activeWellIds, [engineerGrant], [engineerRole, engineerRole]).Allowed ||
    ScopedGrantEvaluator.Authorize(
        grantUserId, true, WellSitePermissions.AssetsRead, Guid.Empty,
        activeWellIds, [engineerGrant], [engineerRole]).Allowed)
{
    throw new InvalidOperationException("Ambiguous role or invalid target authorized access.");
}

// Exercise the product catalog so a new permission cannot silently inherit
// the wrong scope semantics from an otherwise privileged role.
foreach (var permission in ProductPermissionCatalog.Definitions)
{
    var role = mixedRole with { Permissions = [permission.Key] };
    var resourceScoped = permission.ScopeKind == "ResourceScoped";
    var grant = globalLogicGrant with
    {
        RoleId = role.Id,
        ScopeKind = resourceScoped ? AccessGrantScopeKind.ExactAssets : AccessGrantScopeKind.DeploymentWide,
        AssetIds = resourceScoped ? [wellA] : []
    };
    var allowed = ScopedGrantEvaluator.Authorize(
        grantUserId, true, permission.Key, resourceScoped ? wellA : null,
        activeWellIds, [grant], [role]);
    var wrongTarget = ScopedGrantEvaluator.Authorize(
        grantUserId, true, permission.Key, resourceScoped ? null : wellA,
        activeWellIds, [grant], [role]);
    if (!allowed.Allowed || allowed.GrantedByAssignmentId != grant.Id ||
        allowed.GrantedBySubjectKind != AccessGrantSubjectKind.User || wrongTarget.Allowed)
    {
        throw new InvalidOperationException($"Permission scope mismatch: {permission.Key}.");
    }
}

WindowsGroupGrantChecks.Run();
LegacyGrantMigrationPlannerChecks.Run();

Console.WriteLine("Pure scoped-grant isolation and fail-closed checks passed.");
return 0;
