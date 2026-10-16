using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Recommendations;
using WellSiteAutoPilot.Persistence.Security;

internal static class ScopedResourceQueryChecks
{
    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public static void VerifyTranslation()
    {
        var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=unused;Password=unused")
            .Options;
        using var db = new WellSiteAutoPilotDbContext(options);
        var user = Guid.NewGuid();
        IQueryable[] queries =
        [
            ScopedAccessQueries.Assets(db, user, WellSitePermissions.AssetsRead)
                .OrderBy(asset => asset.Name).Skip(1).Take(1),
            ScopedAccessQueries.ConfiguredLogic(db, user, WellSitePermissions.ConfiguredLogicRead)
                .OrderBy(definition => definition.Name).Skip(1).Take(1),
            ScopedAccessQueries.Executions(db, user, WellSitePermissions.ExecutionsRead)
                .OrderByDescending(execution => execution.RequestedAtUtc).Skip(1).Take(1),
            ScopedAccessQueries.Recommendations(db, user, WellSitePermissions.RecommendationsRead)
                .OrderByDescending(recommendation => recommendation.CreatedAtUtc).Skip(1).Take(1)
        ];
        foreach (var query in queries)
        {
            // ToQueryString translates with the real Npgsql provider without
            // connecting. PostgreSQL integration below verifies actual rows.
            var sql = query.ToQueryString();
            if (!sql.Contains("EXISTS", StringComparison.Ordinal) ||
                !sql.Contains("LIMIT", StringComparison.Ordinal) ||
                !sql.Contains("OFFSET", StringComparison.Ordinal))
                throw new InvalidOperationException("Resource scope/paging did not translate to PostgreSQL.");
        }
        foreach (var query in new IQueryable[]
        {
            ScopedAccessQueries.Permissions(db, user),
            ScopedAccessQueries.ResourceGrants(db, user, WellSitePermissions.AssetsManage),
            ScopedAccessQueries.DeploymentGrants(db, user, WellSitePermissions.SecurityManage),
            ScopedAccessQueries.AdministratorGrants(db, user)
        })
            if (!query.ToQueryString().Contains("EXISTS", StringComparison.Ordinal))
                throw new InvalidOperationException("Grant authority did not translate to PostgreSQL.");
        Console.WriteLine("All four composed grant/resource queries translated with Npgsql.");
    }

    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options,
        Guid assetTypeId)
    {
        await using var db = new WellSiteAutoPilotDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var identity = $"grant-query-{userId:N}";
        db.Users.Add(new UserEntity
        {
            Id = userId, IdentityKey = identity, IdentityName = identity,
            NormalizedIdentityName = identity.ToUpperInvariant(), IsActive = true,
            CreatedAtUtc = now, LastSeenAtUtc = now
        });
        // Existing suite Assets already have legitimate configurations and
        // executions. Own these targets so full-query assertions measure only
        // this fixture without masking results behind an ID filter.
        var assetA = Guid.NewGuid();
        var assetB = Guid.NewGuid();
        var assetC = Guid.NewGuid();
        foreach (var (id, name) in new[]
        {
            (assetA, "100 Engineer grant-query Asset"),
            (assetB, "200 Reader grant-query Asset"),
            (assetC, "000 Denied grant-query Asset")
        })
            db.Assets.Add(new AssetEntity
            {
                Id = id, AssetTypeId = assetTypeId, Name = name,
                IsActive = true, CreatedAtUtc = now
            });
        var readPermissions = new[]
        {
            WellSitePermissions.AssetsRead, WellSitePermissions.ConfiguredLogicRead,
            WellSitePermissions.ExecutionsRead, WellSitePermissions.RecommendationsRead
        };
        var engineer = Role(readPermissions.Concat(new[]
        {
            WellSitePermissions.LogicRead, WellSitePermissions.AssetsManage, WellSitePermissions.ConfiguredLogicManage,
            WellSitePermissions.ExecutionsRequest, WellSitePermissions.RecommendationsDecide
        }).ToArray());
        var reader = Role(readPermissions);
        Grant(engineer, "ExactAssets", assetA);
        var grantB = Grant(reader, "ExactAssets", assetB);
        // Neither a global grant nor an unverified group may confer resource access.
        Grant(engineer, "DeploymentWide");
        db.AccessGrants.Add(new AccessGrantEntity
        {
            Id = Guid.NewGuid(), SubjectKind = "WindowsGroup", GroupSid = "S-1-5-21-777",
            RoleId = engineer, ScopeKind = "AllAssets", IsActive = true, Revision = 1,
            CreatedAtUtc = now, UpdatedAtUtc = now
        });

        var denied = Configuration("000 Denied", assetC);
        var allowed = Configuration("100 Allowed", assetA);
        var mixed = Configuration("200 Mixed", assetA, assetB);
        var history = Configuration("010 Historical leak", assetA);
        Revision(history.Id, 2, assetC);
        var inputLeak = Configuration("020 Input leak", assetA);
        db.ConfiguredLogicDataBindings.Add(new ConfiguredLogicDataBindingEntity
        {
            RevisionId = inputLeak.Revision, RequirementId = "outside-target",
            AssetId = assetC, ProviderId = "simulator", ProviderAssetExternalId = "outside"
        });
        Configuration("030 Empty revision");
        var partial = Configuration("040 Empty historical revision", assetA);
        Revision(partial.Id, 2);
        var noRevisions = Guid.NewGuid();
        db.ConfiguredLogicDefinitions.Add(new ConfiguredLogicEntity
        {
            Id = noRevisions, Name = "050 No revisions", CreatedAtUtc = now
        });

        var deniedExecution = Execution(denied.Id, denied.Revision, 2, null, assetC);
        var allowedExecution = Execution(allowed.Id, allowed.Revision, 2, null, assetA);
        var mixedExecution = Execution(mixed.Id, mixed.Revision, 2, null, assetA, assetB);
        var v1Execution = Execution(allowed.Id, allowed.Revision, 1, assetA, assetA);
        Execution(allowed.Id, allowed.Revision, 1, assetC, assetA); // Column disclosure outside scope.
        Execution(allowed.Id, allowed.Revision, 1, null, assetA); // Missing V1 target.
        Execution(allowed.Id, allowed.Revision, 2, null); // Missing immutable scope.
        Execution(allowed.Id, allowed.Revision, 2, null, assetA, Guid.NewGuid()); // Missing Asset.
        Execution(allowed.Id, allowed.Revision, 3, null, assetA); // Unknown contract.
        var recommendationC = Recommendation(denied, deniedExecution, assetC, now.AddMinutes(2));
        var recommendationA = Recommendation(allowed, allowedExecution, assetA, now);
        var recommendationB = Recommendation(mixed, mixedExecution, assetB, now.AddMinutes(-1));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await AssertVisibleAsync();
        var accessClock = new AdjustableTimeProvider(now);
        var access = new RequestAccessRepository(db, accessClock);
        var directSubject = new AccessSubject(userId);
        var expectedHints = readPermissions.Concat(new[]
        {
            WellSitePermissions.LogicRead, WellSitePermissions.AssetsManage,
            WellSitePermissions.ConfiguredLogicManage, WellSitePermissions.ExecutionsRequest,
            WellSitePermissions.RecommendationsDecide
        }).ToHashSet(StringComparer.Ordinal);
        if (!(await access.GetPermissionHintsAsync(directSubject)).ToHashSet().SetEquals(expectedHints) ||
            !await access.HasAssetPermissionAsync(directSubject, WellSitePermissions.AssetsManage, [assetA]) ||
            await access.HasAssetPermissionAsync(directSubject, WellSitePermissions.AssetsManage, [assetA, assetB]) ||
            await access.HasAssetPermissionAsync(directSubject, WellSitePermissions.AssetsRead, []) ||
            await access.HasAssetPermissionAsync(directSubject, WellSitePermissions.AssetsRead, [assetA, Guid.Empty]) ||
            await access.HasAssetPermissionAsync(directSubject, WellSitePermissions.LogicRead, [assetA]) ||
            await access.HasDeploymentPermissionAsync(directSubject, WellSitePermissions.AssetsRead) ||
            !await access.HasDeploymentPermissionAsync(directSubject, WellSitePermissions.LogicRead))
            throw new InvalidOperationException("Request permission hints/resource checks lost paired-grant semantics.");
        var groupUser = Guid.NewGuid();
        var groupIdentity = $"group-query-{groupUser:N}";
        db.Users.Add(new UserEntity
        {
            Id = groupUser, IdentityKey = groupIdentity, IdentityName = groupIdentity,
            NormalizedIdentityName = groupIdentity.ToUpperInvariant(), IsActive = true,
            CreatedAtUtc = now, LastSeenAtUtc = now
        });
        const string groupA = "S-1-5-21-801";
        const string groupB = "S-1-5-21-802";
        var groupGrantA = GroupGrant(engineer, groupA, "ExactAssets", assetA);
        GroupGrant(reader, groupB, "ExactAssets", assetB);
        GroupGrant(engineer, groupA, "DeploymentWide");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var membership = new VerifiedWindowsGroupMembership(groupUser, [groupA, groupB], now);
        var groupSubject = new AccessSubject(groupUser, membership);
        if (!(await access.GetPermissionHintsAsync(groupSubject)).ToHashSet().SetEquals(expectedHints))
            throw new InvalidOperationException("Multiple verified groups did not compose permission hints.");
        accessClock.Now = now.Add(VerifiedWindowsGroupMembership.MaximumAge).AddTicks(1);
        if ((await access.GetPermissionHintsAsync(groupSubject)).Count != 0)
            throw new InvalidOperationException("Request authority cached expired group permission hints.");
        accessClock.Now = now.AddTicks(-1);
        if ((await access.GetPermissionHintsAsync(groupSubject)).Count != 0)
            throw new InvalidOperationException("Future group proof authorized coarse permissions.");
        accessClock.Now = now;
        await AssertEmptyAsync(groupUser); // Stored group assignments alone prove nothing.
        await AssertVisibleAsync(groupUser, membership, now);
        if (!await ScopedAccessQueries.DeploymentGrants(db, groupUser, WellSitePermissions.LogicRead,
                membership, now).AnyAsync() ||
            await ScopedAccessQueries.DeploymentGrants(db, groupUser, WellSitePermissions.AssetsRead,
                membership, now).AnyAsync())
            throw new InvalidOperationException("Group deployment-wide grant lost permission scope semantics.");
        await AssertEmptyAsync(groupUser, membership: membership,
            at: now.Add(VerifiedWindowsGroupMembership.MaximumAge).AddTicks(1));
        await AssertEmptyAsync(groupUser, membership: new(userId, [groupA, groupB], now), at: now);
        var oneGroup = new VerifiedWindowsGroupMembership(groupUser, [groupA], now);
        if (await ScopedAccessQueries.Assets(db, groupUser, WellSitePermissions.AssetsRead,
                oneGroup, now).AnyAsync(asset => asset.Id == assetB))
            throw new InvalidOperationException("Removed group membership continued granting access.");
        await db.AccessGrants.Where(grant => grant.Id == groupGrantA)
            .ExecuteUpdateAsync(setters => setters.SetProperty(grant => grant.IsActive, false));
        if (await ScopedAccessQueries.Assets(db, groupUser, WellSitePermissions.AssetsManage,
                membership, now).AnyAsync())
            throw new InvalidOperationException("Revoked group assignment remained effective.");
        await db.AccessGrants.Where(grant => grant.Id == groupGrantA)
            .ExecuteUpdateAsync(setters => setters.SetProperty(grant => grant.IsActive, true));
        db.AccessGrants.Add(new AccessGrantEntity
        {
            Id = Guid.NewGuid(), UserId = groupUser, RoleId = reader, ScopeKind = "ExactAssets",
            IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
        });
        var directReader = db.AccessGrants.Local.Single(grant => grant.UserId == groupUser);
        db.AccessGrantAssets.Add(new AccessGrantAssetEntity { GrantId = directReader.Id, AssetId = assetB });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await AssertVisibleAsync(groupUser, oneGroup, now); // Direct B + group A.
        accessClock.Now = now.AddMinutes(2);
        if (!(await access.GetPermissionHintsAsync(groupSubject)).ToHashSet().SetEquals(readPermissions) ||
            !await access.HasAssetPermissionAsync(groupSubject, WellSitePermissions.AssetsRead, [assetB]) ||
            await access.HasAssetPermissionAsync(groupSubject, WellSitePermissions.AssetsManage, [assetA]))
            throw new InvalidOperationException("Expired groups suppressed valid direct access or retained group authority.");
        accessClock.Now = now;
        await db.Users.Where(user => user.Id == groupUser)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsActive, false));
        await AssertEmptyAsync(groupUser, membership: membership, at: now);

        // A known permission from a different family must not authorize a query.
        if (await ScopedAccessQueries.ConfiguredLogic(db, userId, WellSitePermissions.AssetsRead).AnyAsync() ||
            await ScopedAccessQueries.Executions(db, userId, WellSitePermissions.AssetsRead).AnyAsync() ||
            await ScopedAccessQueries.Recommendations(db, userId, WellSitePermissions.AssetsRead).AnyAsync())
            throw new InvalidOperationException("A foreign-family permission authorized resource disclosure.");
        foreach (var subject in new[] { Guid.Empty, Guid.NewGuid() })
            await AssertEmptyAsync(subject);
        await AssertEmptyAsync(userId, "unknown.permission");

        // Execute the same composed query again after revocation: no retained ID list/cache.
        var executionQuery = ScopedAccessQueries.Executions(db, userId, WellSitePermissions.ExecutionsRead);
        await db.AccessGrants.Where(grant => grant.Id == grantB)
            .ExecuteUpdateAsync(setters => setters.SetProperty(grant => grant.IsActive, false));
        if (await executionQuery.AnyAsync(execution => execution.Id == mixedExecution) ||
            await ScopedAccessQueries.ConfiguredLogic(db, userId, WellSitePermissions.ConfiguredLogicRead)
                .AnyAsync(definition => definition.Id == mixed.Id) ||
            await ScopedAccessQueries.Recommendations(db, userId, WellSitePermissions.RecommendationsRead)
                .AnyAsync(recommendation => recommendation.Id == recommendationB))
            throw new InvalidOperationException("Revoked grant remained visible through composed SQL.");
        await db.AccessGrants.Where(grant => grant.Id == grantB)
            .ExecuteUpdateAsync(setters => setters.SetProperty(grant => grant.IsActive, true));

        await db.Assets.Where(asset => asset.Id == assetB)
            .ExecuteUpdateAsync(setters => setters.SetProperty(asset => asset.IsActive, false));
        if (await executionQuery.AnyAsync(execution => execution.Id == mixedExecution))
            throw new InvalidOperationException("Inactive participating Asset authorized an execution.");
        await db.Assets.Where(asset => asset.Id == assetB)
            .ExecuteUpdateAsync(setters => setters.SetProperty(asset => asset.IsActive, true));
        await db.RolePermissions.Where(permission => permission.RoleId == engineer &&
                permission.Permission == WellSitePermissions.ExecutionsRead).ExecuteDeleteAsync();
        if (await executionQuery.AnyAsync())
            throw new InvalidOperationException("Revoked role permission remained visible.");
        db.RolePermissions.Add(new RolePermissionEntity
        {
            RoleId = engineer, Permission = WellSitePermissions.ExecutionsRead
        });
        await db.SaveChangesAsync();
        await db.RoleDefinitions.Where(role => role.Id == engineer)
            .ExecuteUpdateAsync(setters => setters.SetProperty(role => role.IsActive, false));
        if (await executionQuery.AnyAsync())
            throw new InvalidOperationException("Inactive role authorized execution access.");
        await db.RoleDefinitions.Where(role => role.Id == engineer)
            .ExecuteUpdateAsync(setters => setters.SetProperty(role => role.IsActive, true));
        foreach (var invalidRevision in new long[] { 0, -1 })
        {
            await db.RoleDefinitions.Where(role => role.Id == engineer || role.Id == reader)
                .ExecuteUpdateAsync(setters => setters.SetProperty(role => role.Revision, invalidRevision));
            await AssertEmptyAsync(userId);
            if ((await access.GetPermissionHintsAsync(directSubject)).Count != 0)
                throw new InvalidOperationException("Invalid role revision authorized coarse permissions.");
            if (await ScopedAccessQueries.DeploymentGrants(db, userId, WellSitePermissions.LogicRead).AnyAsync())
                throw new InvalidOperationException("Invalid role revision authorized a deployment permission.");
        }
        await db.RoleDefinitions.Where(role => role.Id == engineer || role.Id == reader)
            .ExecuteUpdateAsync(setters => setters.SetProperty(role => role.Revision, 1L));
        await AssertVisibleAsync();
        await db.Users.Where(user => user.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsActive, false));
        await AssertEmptyAsync(userId);
        if ((await access.GetPermissionHintsAsync(directSubject)).Count != 0)
            throw new InvalidOperationException("Inactive user retained coarse permission hints.");
        await db.Users.Where(user => user.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsActive, true));
        await AssertVisibleAsync();
        // An explicit AllAssets grant remains a usable coarse prerequisite
        // before the first active Asset exists. It still cannot confer global
        // capabilities, and a malformed binding must invalidate the grant.
        var initialRole = Role([WellSitePermissions.AssetsManage, WellSitePermissions.SecurityManage, "future.unknown"]);
        var initialGrant = Grant(initialRole, "AllAssets");
        await db.SaveChangesAsync();
        await db.Assets.ExecuteUpdateAsync(setters => setters.SetProperty(asset => asset.IsActive, false));
        var emptyHints = await access.GetPermissionHintsAsync(directSubject);
        if (!emptyHints.Contains(WellSitePermissions.AssetsManage) ||
            emptyHints.Contains(WellSitePermissions.SecurityManage) || emptyHints.Contains("future.unknown") ||
            await ScopedAccessQueries.Assets(db, userId, WellSitePermissions.AssetsManage).AnyAsync() ||
            !await ScopedAccessQueries.ResourceGrants(db, userId, WellSitePermissions.AssetsManage).AnyAsync())
            throw new InvalidOperationException("Empty deployment lost explicit AllAssets authority or implied global access.");
        db.AccessGrantAssets.Add(new AccessGrantAssetEntity { GrantId = initialGrant, AssetId = assetA });
        await db.SaveChangesAsync();
        if ((await access.GetPermissionHintsAsync(directSubject)).Contains(WellSitePermissions.AssetsManage))
            throw new InvalidOperationException("Malformed AllAssets binding retained coarse permission hints.");
        await transaction.RollbackAsync();
        Console.WriteLine("Grant-scoped resource SQL: paired permission, all revisions/inputs, V1/V2 scope, pagination and revocation passed.");

        async Task AssertVisibleAsync(Guid? subjectId = null,
            VerifiedWindowsGroupMembership? membership = null, DateTimeOffset? at = null)
        {
            var subject = subjectId ?? userId;
            var assets = ScopedAccessQueries.Assets(db, subject, WellSitePermissions.AssetsRead, membership, at);
            if ((await assets.CountAsync()) != 2 ||
                await assets.OrderBy(asset => asset.Name).Take(1).AnyAsync(asset => asset.Id == assetC))
                throw new InvalidOperationException("Asset filtering did not precede pagination.");
            var configs = ScopedAccessQueries.ConfiguredLogic(db, subject, WellSitePermissions.ConfiguredLogicRead, membership, at);
            if (!(await configs.Select(item => item.Id).ToArrayAsync()).ToHashSet().SetEquals([allowed.Id, mixed.Id]) ||
                (await configs.OrderBy(item => item.Name).Take(1).SingleAsync()).Id != allowed.Id ||
                (await configs.OrderBy(item => item.Name).Skip(1).Take(1).SingleAsync()).Id != mixed.Id)
                throw new InvalidOperationException("Configuration history/input scope escaped SQL or displaced a valid page.");
            if ((await ScopedAccessQueries.ConfiguredLogic(db, subject, WellSitePermissions.ConfiguredLogicManage, membership, at)
                    .Select(item => item.Id).SingleAsync()) != allowed.Id)
                throw new InvalidOperationException("Manage permission crossed between complete grants.");
            var executions = ScopedAccessQueries.Executions(db, subject, WellSitePermissions.ExecutionsRead, membership, at);
            if (!(await executions.Select(item => item.Id).ToArrayAsync()).ToHashSet()
                    .SetEquals([allowedExecution, mixedExecution, v1Execution]) ||
                await executions.OrderByDescending(item => item.RequestedAtUtc).Take(1)
                    .AnyAsync(item => item.Id == deniedExecution))
                throw new InvalidOperationException("Execution snapshot scope escaped SQL or displaced a valid page.");
            if (await ScopedAccessQueries.Executions(db, subject, WellSitePermissions.ExecutionsRequest, membership, at)
                    .AnyAsync(item => item.Id == mixedExecution))
                throw new InvalidOperationException("Request permission leaked from A to B.");
            var recommendations = ScopedAccessQueries.Recommendations(db, subject, WellSitePermissions.RecommendationsRead, membership, at);
            if (!(await recommendations.Select(item => item.Id).ToArrayAsync()).ToHashSet()
                    .SetEquals([recommendationA, recommendationB]) ||
                (await recommendations.OrderByDescending(item => item.CreatedAtUtc).Take(1).SingleAsync()).Id != recommendationA ||
                (await ScopedAccessQueries.Recommendations(db, subject, WellSitePermissions.RecommendationsDecide, membership, at)
                    .Select(item => item.Id).SingleAsync()) != recommendationA)
                throw new InvalidOperationException("Recommendation pagination or paired decision scope failed.");

            var request = new AccessSubject(subject, membership);
            var requestClock = new AdjustableTimeProvider(at ?? now);
            var assetRepository = new AssetRepository(db, requestClock);
            var configuredRepository = new ConfiguredLogicRepository(db, requestClock);
            var executionRepository = new ExecutionRepository(db, requestClock);
            var recommendationRepository = new RecommendationRepository(db, requestClock);
            if ((await assetRepository.ListAuthorizedAsync(request, null, null, 1)).Single().Id != assetA ||
                await assetRepository.GetAuthorizedAsync(request, assetC) is not null ||
                (await assetRepository.GetAuthorizedAsync(request, assetB))?.Id != assetB)
                throw new InvalidOperationException("Asset repository lost authorized detail/page semantics.");
            if ((await configuredRepository.ListAuthorizedAsync(request, 1)).Single().Id != allowed.Id ||
                await configuredRepository.GetAuthorizedAsync(request, history.Id) is not null ||
                await configuredRepository.GetAuthorizedAsync(request, inputLeak.Id) is not null ||
                (await configuredRepository.GetAuthorizedAsync(request, mixed.Id))?.Revisions.Count != 1)
                throw new InvalidOperationException("Configured Logic repository disclosed unchecked revisions or displaced an authorized page.");
            if (!(await executionRepository.ListAuthorizedAsync(request, null, 100)).Select(item => item.Id)
                    .ToHashSet().SetEquals([allowedExecution, mixedExecution, v1Execution]) ||
                await executionRepository.GetAuthorizedAsync(request, deniedExecution) is not null ||
                (await executionRepository.GetAuthorizedAsync(request, v1Execution))?.Id != v1Execution)
                throw new InvalidOperationException("Execution repository lost V1/V2 authorized snapshot semantics.");
            if ((await recommendationRepository.ListAuthorizedAsync(request, null, 1)).Single().Id != recommendationA ||
                await recommendationRepository.GetAuthorizedAsync(request, recommendationC) is not null ||
                (await recommendationRepository.GetAuthorizedAsync(request, recommendationB))?.Id != recommendationB)
                throw new InvalidOperationException("Recommendation repository lost authorized detail/page semantics.");
        }

        async Task AssertEmptyAsync(Guid subject, string? permission = null,
            VerifiedWindowsGroupMembership? membership = null, DateTimeOffset? at = null)
        {
            if (await ScopedAccessQueries.Assets(db, subject, permission ?? WellSitePermissions.AssetsRead, membership, at).AnyAsync() ||
                await ScopedAccessQueries.ConfiguredLogic(db, subject, permission ?? WellSitePermissions.ConfiguredLogicRead, membership, at).AnyAsync() ||
                await ScopedAccessQueries.Executions(db, subject, permission ?? WellSitePermissions.ExecutionsRead, membership, at).AnyAsync() ||
                await ScopedAccessQueries.Recommendations(db, subject, permission ?? WellSitePermissions.RecommendationsRead, membership, at).AnyAsync())
                throw new InvalidOperationException("Invalid/inactive identity or permission authorized a resource.");
            if (permission is null)
            {
                // Do not construct a trusted subject with a mismatched proof;
                // the host rejects that before any repository is called.
                if (subject == Guid.Empty || (membership is not null && membership.UserId != subject)) return;
                var request = new AccessSubject(subject, membership);
                var requestClock = new AdjustableTimeProvider(at ?? now);
                if ((await new AssetRepository(db, requestClock).ListAuthorizedAsync(request, null, null, 100)).Count != 0 ||
                    (await new ConfiguredLogicRepository(db, requestClock).ListAuthorizedAsync(request, 100)).Count != 0 ||
                    (await new ExecutionRepository(db, requestClock).ListAuthorizedAsync(request, null, 100)).Count != 0 ||
                    (await new RecommendationRepository(db, requestClock).ListAuthorizedAsync(request, null, 100)).Count != 0)
                    throw new InvalidOperationException("Invalid/inactive subject was disclosed by a resource repository.");
            }
        }

        Guid Role(string[] permissions)
        {
            var id = Guid.NewGuid();
            db.RoleDefinitions.Add(new RoleDefinitionEntity
            {
                Id = id, Key = id.ToString("N"), Name = id.ToString("N"),
                NormalizedName = id.ToString("N").ToUpperInvariant(), IsActive = true,
                Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            });
            db.RolePermissions.AddRange(permissions.Select(permission =>
                new RolePermissionEntity { RoleId = id, Permission = permission }));
            return id;
        }

        Guid GroupGrant(Guid roleId, string sid, string scopeKind, params Guid[] assets)
        {
            var id = Guid.NewGuid();
            db.AccessGrants.Add(new AccessGrantEntity
            {
                Id = id, SubjectKind = "WindowsGroup", GroupSid = sid, RoleId = roleId,
                ScopeKind = scopeKind, IsActive = true, Revision = 1,
                CreatedAtUtc = now, UpdatedAtUtc = now
            });
            db.AccessGrantAssets.AddRange(assets.Select(asset =>
                new AccessGrantAssetEntity { GrantId = id, AssetId = asset }));
            return id;
        }

        Guid Grant(Guid roleId, string scopeKind, params Guid[] assets)
        {
            var id = Guid.NewGuid();
            db.AccessGrants.Add(new AccessGrantEntity
            {
                Id = id, UserId = userId, RoleId = roleId, ScopeKind = scopeKind,
                IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            });
            db.AccessGrantAssets.AddRange(assets.Select(asset =>
                new AccessGrantAssetEntity { GrantId = id, AssetId = asset }));
            return id;
        }

        (Guid Id, Guid Revision) Configuration(string name, params Guid[] assets)
        {
            var id = Guid.NewGuid();
            db.ConfiguredLogicDefinitions.Add(new ConfiguredLogicEntity
            {
                Id = id, Name = name, CreatedAtUtc = now
            });
            return (id, Revision(id, 1, assets));
        }

        Guid Revision(Guid configuration, int number, params Guid[] assets)
        {
            var id = Guid.NewGuid();
            db.ConfiguredLogicRevisions.Add(new ConfiguredLogicRevisionEntity
            {
                Id = id, ConfiguredLogicId = configuration, RevisionNumber = number,
                ModuleId = "query-test", ModuleVersion = "1.0.0", Mode = "Shadow",
                Status = "Draft", CreatedAtUtc = now
            });
            db.ConfiguredLogicAssetBindings.AddRange(assets.Select(asset =>
                new ConfiguredLogicAssetBindingEntity { RevisionId = id, Role = "target", AssetId = asset }));
            return id;
        }

        Guid Execution(Guid configuration, Guid revision, int version, Guid? target, params Guid[] assets)
        {
            var id = Guid.NewGuid();
            db.Executions.Add(new ExecutionEntity
            {
                Id = id, LogicInstanceId = configuration, ConfigurationRevisionId = revision,
                ModuleId = "query-test", ModuleVersion = "1.0.0", Mode = "Shadow", Status = "Completed",
                AssetId = target, RequestContractVersion = version, Trigger = "Manual",
                RequestedAtUtc = assets.Contains(assetC) ? now.AddMinutes(2) : now
            });
            db.ExecutionAssetScopes.AddRange(assets.Select(asset =>
                new ExecutionAssetScopeEntity { ExecutionId = id, LogicInstanceId = configuration, AssetId = asset }));
            return id;
        }

        Guid Recommendation((Guid Id, Guid Revision) configuration, Guid execution, Guid asset, DateTimeOffset created)
        {
            var id = Guid.NewGuid();
            db.Recommendations.Add(new RecommendationEntity
            {
                Id = id, ExecutionId = execution, ConfiguredLogicId = configuration.Id,
                ConfigurationRevisionId = configuration.Revision, AssetId = asset,
                ModuleId = "query-test", ModuleVersion = "1.0.0", Code = "QUERY_TEST",
                Command = "test", ReasonCode = "TEST", Status = "Pending", CreatedAtUtc = created
            });
            return id;
        }
    }
}
