using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Security;

internal static class ScopedSqlProjectionChecks
{
    public static async Task RunAsync(
        DbContextOptions<WellSiteAutoPilotDbContext> options,
        Guid userId, Guid otherUserId, Guid assetA, Guid assetB)
    {
        await using var db = new WellSiteAutoPilotDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var sql = new ProposedAssetAccessRepository(db);
        var now = DateTimeOffset.UtcNow;
        var engineer = Role("SQL Engineer", [WellSitePermissions.AssetsRead,
            WellSitePermissions.AssetsManage, WellSitePermissions.LogicRead]);
        var reader = Role("SQL Reader", [WellSitePermissions.AssetsRead]);
        var exact = Grant(engineer, "ExactAssets", assetA);
        _ = Grant(reader, "ExactAssets", assetB);
        var global = Grant(engineer, "DeploymentWide");
        db.AccessGrants.Add(new AccessGrantEntity
        {
            Id = Guid.NewGuid(), SubjectKind = "WindowsGroup", UserId = null,
            GroupSid = "S-1-5-21-100", RoleId = engineer, ScopeKind = "AllAssets",
            IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await VerifyParityAsync(userId);
        await VerifyParityAsync(otherUserId);
        await VerifyParityAsync(Guid.NewGuid());

        // An unauthorized resource sorts before both authorized resources.
        // Filtering after Skip/Take would omit a permitted row from page one.
        var assetType = await db.Assets.Where(item => item.Id == assetA)
            .Select(item => item.AssetTypeId).SingleAsync();
        db.Assets.Add(new AssetEntity
        {
            Id = Guid.NewGuid(), AssetTypeId = assetType,
            Name = "000 Unauthorized SQL projection Asset", IsActive = true, CreatedAtUtc = now
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var expected = await db.Assets.AsNoTracking()
            .Where(item => item.Id == assetA || item.Id == assetB)
            .OrderBy(item => item.Name).ThenBy(item => item.Id)
            .Select(item => item.Id).ToArrayAsync();
        var firstPage = await sql.ListAsync(userId, WellSitePermissions.AssetsRead, 0, 1);
        var secondPage = await sql.ListAsync(userId, WellSitePermissions.AssetsRead, 1, 1);
        if (!firstPage.Concat(secondPage).Select(item => item.Id).SequenceEqual(expected) ||
            (await sql.ListAsync(userId, WellSitePermissions.AssetsManage, 0, 1)).Single().Id != assetA)
        {
            throw new InvalidOperationException("SQL authorization was not applied before paging.");
        }

        foreach (var malformedSet in new Guid[][]
        {
            [], [assetA, assetA], [assetA, Guid.Empty], [assetA, Guid.NewGuid()]
        })
        {
            if (await sql.AreAssetsAuthorizedAsync(userId, WellSitePermissions.AssetsRead, malformedSet))
            {
                throw new InvalidOperationException("Malformed bulk Asset request authorized access.");
            }
        }
        if ((await sql.ListAsync(userId, "unknown.permission", 0, 10)).Count != 0 ||
            (await sql.ListAsync(userId, WellSitePermissions.AssetsRead, -1, 10)).Count != 0 ||
            (await sql.ListAsync(userId, WellSitePermissions.AssetsRead, 0, 501)).Count != 0)
        {
            throw new InvalidOperationException("Invalid SQL projection query returned Assets.");
        }

        await db.RoleDefinitions.Where(item => item.Id == engineer)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, false));
        await VerifyParityAsync(userId);
        await db.RoleDefinitions.Where(item => item.Id == engineer)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, true));
        await db.Users.Where(item => item.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, false));
        await VerifyParityAsync(userId);
        await db.Users.Where(item => item.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, true));

        // One inactive binding invalidates the entire exact grant, even when
        // the requested Asset itself is still active.
        db.AccessGrantAssets.Add(new AccessGrantAssetEntity { GrantId = exact, AssetId = assetB });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await db.Assets.Where(item => item.Id == assetB)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, false));
        await VerifyParityAsync(userId);
        if (await sql.IsAssetAuthorizedAsync(userId, WellSitePermissions.AssetsManage, assetA))
        {
            throw new InvalidOperationException("An inactive binding failed to invalidate an exact grant.");
        }
        await db.Assets.Where(item => item.Id == assetB)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, true));
        await db.AccessGrantAssets.Where(item => item.GrantId == exact && item.AssetId == assetB)
            .ExecuteDeleteAsync();

        // A global grant carrying an Asset binding is malformed and cannot
        // confer deployment-wide permissions. It never confers Asset access.
        db.AccessGrantAssets.Add(new AccessGrantAssetEntity { GrantId = global, AssetId = assetB });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await VerifyParityAsync(userId);
        if (await sql.IsDeploymentPermissionGrantedAsync(userId, WellSitePermissions.LogicRead))
        {
            throw new InvalidOperationException("Malformed global grant authorized a permission.");
        }
        await db.AccessGrantAssets.Where(item => item.GrantId == global).ExecuteDeleteAsync();

        // Positive maximum and negative overflow must match the pure model.
        var extraAssets = Enumerable.Range(0, ScopedGrantEvaluator.MaximumExactAssetsPerGrant)
            .Select(index => new AssetEntity
            {
                Id = Guid.NewGuid(), AssetTypeId = assetType, Name = $"SQL Scope {index:D4}",
                IsActive = true, CreatedAtUtc = now
            }).ToArray();
        db.Assets.AddRange(extraAssets);
        db.AccessGrantAssets.AddRange(extraAssets.Take(499).Select(item =>
            new AccessGrantAssetEntity { GrantId = exact, AssetId = item.Id }));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await VerifyParityAsync(userId);
        if (!await sql.IsAssetAuthorizedAsync(userId, WellSitePermissions.AssetsManage, assetA))
        {
            throw new InvalidOperationException("A valid maximum-sized exact grant was denied.");
        }
        db.AccessGrantAssets.Add(new AccessGrantAssetEntity { GrantId = exact, AssetId = extraAssets[^1].Id });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await VerifyParityAsync(userId);
        if (await sql.IsAssetAuthorizedAsync(userId, WellSitePermissions.AssetsManage, assetA))
        {
            throw new InvalidOperationException("An oversized exact grant authorized access.");
        }

        await db.AccessGrants.Where(item => item.Id == exact)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ScopeKind, "AllAssets"));
        await VerifyParityAsync(userId); // AllAssets with bindings remains malformed.
        await db.AccessGrantAssets.Where(item => item.GrantId == exact).ExecuteDeleteAsync();
        await VerifyParityAsync(userId);
        if (!await sql.IsAssetAuthorizedAsync(userId, WellSitePermissions.AssetsManage, assetB) ||
            await sql.IsDeploymentPermissionGrantedAsync(userId, WellSitePermissions.AssetsManage))
        {
            throw new InvalidOperationException("AllAssets and deployment scope were conflated.");
        }
        // A malformed imported Asset with an empty ID must not appear in
        // AllAssets lists when point/bulk and pure decisions reject that ID.
        var emptyAttributes = "{}";
        var emptyName = "000 Empty SQL Asset";
        // Raw SQL deliberately bypasses EF's automatic Guid generation.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO assets.assets
                ("Id", "AssetTypeId", "Name", "AttributeValuesJson", "IsActive", "CreatedAtUtc")
            VALUES ({Guid.Empty}, {assetType}, {emptyName}, {emptyAttributes}, TRUE, {now})
            """);
        if (!await db.Assets.AnyAsync(item => item.Id == Guid.Empty))
        {
            throw new InvalidOperationException("Malformed Asset fixture was not persisted.");
        }
        if ((await sql.ListAsync(userId, WellSitePermissions.AssetsManage, 0, 500))
                .Any(item => item.Id == Guid.Empty) ||
            await sql.IsAssetAuthorizedAsync(userId, WellSitePermissions.AssetsManage, Guid.Empty))
        {
            throw new InvalidOperationException("A malformed empty-ID Asset escaped SQL validation.");
        }
        await db.AccessGrants.Where(item => item.Id == exact)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsActive, false));
        await VerifyParityAsync(userId);
        await transaction.RollbackAsync();

        Guid Role(string name, string[] permissions)
        {
            var id = Guid.NewGuid();
            db.RoleDefinitions.Add(new RoleDefinitionEntity
            {
                Id = id, Key = id.ToString("N"), Name = name,
                NormalizedName = name.ToUpperInvariant(), IsActive = true, Revision = 1,
                CreatedAtUtc = now, UpdatedAtUtc = now
            });
            db.RolePermissions.AddRange(permissions.Select(permission =>
                new RolePermissionEntity { RoleId = id, Permission = permission }));
            return id;
        }

        Guid Grant(Guid roleId, string scope, params Guid[] assets)
        {
            var id = Guid.NewGuid();
            db.AccessGrants.Add(new AccessGrantEntity
            {
                Id = id, UserId = userId, RoleId = roleId, ScopeKind = scope,
                IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            });
            db.AccessGrantAssets.AddRange(assets.Select(asset =>
                new AccessGrantAssetEntity { GrantId = id, AssetId = asset }));
            return id;
        }

        async Task VerifyParityAsync(Guid subjectId)
        {
            var active = await db.Users.AsNoTracking().AnyAsync(item => item.Id == subjectId && item.IsActive);
            var activeAssets = (await db.Assets.AsNoTracking().Where(item => item.IsActive)
                .Select(item => item.Id).ToArrayAsync()).ToHashSet();
            var rows = await db.AccessGrants.AsNoTracking().ToArrayAsync();
            var bindings = await db.AccessGrantAssets.AsNoTracking().ToArrayAsync();
            var permissions = await db.RolePermissions.AsNoTracking().ToArrayAsync();
            var roleRows = await db.RoleDefinitions.AsNoTracking().ToArrayAsync();
            var roles = roleRows.Select(role => new RoleDefinition(role.Id, role.Key,
                role.Name, role.Description, role.IsSystem, role.IsActive, role.Revision,
                role.CreatedAtUtc, role.UpdatedAtUtc,
                permissions.Where(item => item.RoleId == role.Id).Select(item => item.Permission).ToArray())).ToArray();
            var grants = rows.Select(row => new AccessGrant(row.Id,
                Enum.Parse<AccessGrantSubjectKind>(row.SubjectKind), row.UserId, row.GroupSid,
                row.RoleId, Enum.Parse<AccessGrantScopeKind>(row.ScopeKind),
                bindings.Where(item => item.GrantId == row.Id).Select(item => item.AssetId).ToArray(),
                row.IsActive, row.Revision)).ToArray();
            foreach (var permission in ProductPermissionCatalog.Definitions)
            {
                bool Pure(Guid? asset) => ScopedGrantEvaluator.Authorize(subjectId, active,
                    permission.Key, asset, activeAssets, grants, roles).Allowed;
                var a = Pure(assetA);
                var b = Pure(assetB);
                if (await sql.IsAssetAuthorizedAsync(subjectId, permission.Key, assetA) != a ||
                    await sql.IsAssetAuthorizedAsync(subjectId, permission.Key, assetB) != b ||
                    await sql.AreAssetsAuthorizedAsync(subjectId, permission.Key, [assetA, assetB]) != (a && b) ||
                    await sql.IsDeploymentPermissionGrantedAsync(subjectId, permission.Key) != Pure(null))
                {
                    throw new InvalidOperationException($"SQL/pure grant disagreement: {permission.Key}.");
                }
            }
        }
    }
}
