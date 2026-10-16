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
        Console.WriteLine("All four composed grant/resource queries translated with Npgsql.");
    }

    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options,
        Guid assetA, Guid assetB)
    {
        await using var db = new WellSiteAutoPilotDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var identity = $"grant-query-{userId:N}";
        db.Users.Add(new UserEntity
        {
            Id = userId, IdentityKey = identity, IdentityName = identity,
            NormalizedIdentityName = identity.ToUpperInvariant(), IsActive = true,
            CreatedAtUtc = now, LastSeenAtUtc = now
        });
        var assetType = await db.Assets.Where(asset => asset.Id == assetA)
            .Select(asset => asset.AssetTypeId).SingleAsync();
        var assetC = Guid.NewGuid();
        db.Assets.Add(new AssetEntity
        {
            Id = assetC, AssetTypeId = assetType, Name = "000 Denied grant-query Asset",
            IsActive = true, CreatedAtUtc = now
        });
        var readPermissions = new[]
        {
            WellSitePermissions.AssetsRead, WellSitePermissions.ConfiguredLogicRead,
            WellSitePermissions.ExecutionsRead, WellSitePermissions.RecommendationsRead
        };
        var engineer = Role(readPermissions.Concat(new[]
        {
            WellSitePermissions.AssetsManage, WellSitePermissions.ConfiguredLogicManage,
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
        Recommendation(denied, deniedExecution, assetC, now.AddMinutes(2));
        var recommendationA = Recommendation(allowed, allowedExecution, assetA, now);
        var recommendationB = Recommendation(mixed, mixedExecution, assetB, now.AddMinutes(-1));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await AssertVisibleAsync();
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
        await db.Users.Where(user => user.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsActive, false));
        await AssertEmptyAsync(userId);
        await db.Users.Where(user => user.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsActive, true));
        await AssertVisibleAsync();
        await transaction.RollbackAsync();
        Console.WriteLine("Grant-scoped resource SQL: paired permission, all revisions/inputs, V1/V2 scope, pagination and revocation passed.");

        async Task AssertVisibleAsync()
        {
            var assets = ScopedAccessQueries.Assets(db, userId, WellSitePermissions.AssetsRead);
            if ((await assets.CountAsync()) != 2 ||
                await assets.OrderBy(asset => asset.Name).Take(1).AnyAsync(asset => asset.Id == assetC))
                throw new InvalidOperationException("Asset filtering did not precede pagination.");
            var configs = ScopedAccessQueries.ConfiguredLogic(db, userId, WellSitePermissions.ConfiguredLogicRead);
            if (!(await configs.Select(item => item.Id).ToArrayAsync()).ToHashSet().SetEquals([allowed.Id, mixed.Id]) ||
                (await configs.OrderBy(item => item.Name).Take(1).SingleAsync()).Id != allowed.Id ||
                (await configs.OrderBy(item => item.Name).Skip(1).Take(1).SingleAsync()).Id != mixed.Id)
                throw new InvalidOperationException("Configuration history/input scope escaped SQL or displaced a valid page.");
            if ((await ScopedAccessQueries.ConfiguredLogic(db, userId, WellSitePermissions.ConfiguredLogicManage)
                    .Select(item => item.Id).SingleAsync()) != allowed.Id)
                throw new InvalidOperationException("Manage permission crossed between complete grants.");
            var executions = ScopedAccessQueries.Executions(db, userId, WellSitePermissions.ExecutionsRead);
            if (!(await executions.Select(item => item.Id).ToArrayAsync()).ToHashSet()
                    .SetEquals([allowedExecution, mixedExecution, v1Execution]) ||
                await executions.OrderByDescending(item => item.RequestedAtUtc).Take(1)
                    .AnyAsync(item => item.Id == deniedExecution))
                throw new InvalidOperationException("Execution snapshot scope escaped SQL or displaced a valid page.");
            if (await ScopedAccessQueries.Executions(db, userId, WellSitePermissions.ExecutionsRequest)
                    .AnyAsync(item => item.Id == mixedExecution))
                throw new InvalidOperationException("Request permission leaked from A to B.");
            var recommendations = ScopedAccessQueries.Recommendations(db, userId, WellSitePermissions.RecommendationsRead);
            if (!(await recommendations.Select(item => item.Id).ToArrayAsync()).ToHashSet()
                    .SetEquals([recommendationA, recommendationB]) ||
                (await recommendations.OrderByDescending(item => item.CreatedAtUtc).Take(1).SingleAsync()).Id != recommendationA ||
                (await ScopedAccessQueries.Recommendations(db, userId, WellSitePermissions.RecommendationsDecide)
                    .Select(item => item.Id).SingleAsync()) != recommendationA)
                throw new InvalidOperationException("Recommendation pagination or paired decision scope failed.");
        }

        async Task AssertEmptyAsync(Guid subject, string? permission = null)
        {
            if (await ScopedAccessQueries.Assets(db, subject, permission ?? WellSitePermissions.AssetsRead).AnyAsync() ||
                await ScopedAccessQueries.ConfiguredLogic(db, subject, permission ?? WellSitePermissions.ConfiguredLogicRead).AnyAsync() ||
                await ScopedAccessQueries.Executions(db, subject, permission ?? WellSitePermissions.ExecutionsRead).AnyAsync() ||
                await ScopedAccessQueries.Recommendations(db, subject, permission ?? WellSitePermissions.RecommendationsRead).AnyAsync())
                throw new InvalidOperationException("Invalid/inactive identity or permission authorized a resource.");
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
