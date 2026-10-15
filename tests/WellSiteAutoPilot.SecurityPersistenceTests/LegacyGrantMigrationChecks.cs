using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Security;

internal static class LegacyGrantMigrationChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options,
        UserAccessProfile subject, UserAccessProfile demoted, UserAccessProfile administrator,
        Guid assetId, TimeProvider clock)
    {
        await using var db = new WellSiteAutoPilotDbContext(options);
        var users = new UserAccessRepository(db);
        var roles = new RoleCatalogRepository(db);
        var grants = new AccessGrantRepository(db);
        var administration = new AccessGrantAdministrationService(grants, users, roles, clock);
        var repository = new LegacyGrantMigrationRepository(db);
        var service = new LegacyGrantMigrationService(administration, repository, clock);
        var actor = new SecurityActorContext(administrator.Id, administrator.IdentityName, "migration-regression");
        var profilesBefore = await users.ListAsync(101);
        var accessBefore = profilesBefore.ToDictionary(item => item.Id, Fingerprint);
        var custom = await new RoleCatalogService(roles, users, clock).CreateAsync(
            new RoleMutationCommand("Migration Customer Role", "", [WellSitePermissions.AssetsManage], true), actor);

        await RejectAsync(() => service.MigrateAsync(false, actor), "SECURITY_MIGRATION_CONFIRMATION_REQUIRED");
        await RejectAsync(() => service.RollbackAsync(false, actor), "SECURITY_MIGRATION_CONFIRMATION_REQUIRED");
        foreach (var denied in new[] { subject, demoted })
        {
            var identity = new SecurityActorContext(denied.Id, denied.IdentityName, "denied-migration");
            await RejectAsync(() => service.MigrateAsync(true, identity), "SECURITY_ADMIN_REQUIRED");
            await RejectAsync(() => service.RollbackAsync(true, identity), "SECURITY_ADMIN_REQUIRED");
            await RejectAsync(() => repository.MigrateAsync(denied.Id, denied.IdentityName, null, clock.GetUtcNow()), "SECURITY_ADMIN_REQUIRED");
            await RejectAsync(() => repository.RollbackAsync(denied.Id, denied.IdentityName, null, clock.GetUtcNow()), "SECURITY_ADMIN_REQUIRED");
        }
        var customer = await administration.CreateForUserAsync(subject.Id,
            new CreateAccessGrantCommand(custom.Id, AccessGrantScopeKind.ExactAssets, [assetId], custom.Revision), actor);
        await RejectWithoutWritesAsync(() => service.MigrateAsync(true, actor));
        await administration.DeactivateForUserAsync(subject.Id, customer.Id, customer.Revision, actor);

        // Untrusted group assignments cannot enter a direct-user migration.
        var group = new AccessGrantEntity
        {
            Id = Guid.NewGuid(), SubjectKind = "WindowsGroup", GroupSid = "S-1-5-21-98765",
            RoleId = custom.Id, ScopeKind = "AllAssets", IsActive = true, Revision = 1,
            CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow()
        };
        db.AccessGrants.Add(group);
        await db.SaveChangesAsync();
        await RejectWithoutWritesAsync(() => service.MigrateAsync(true, actor));
        await db.AccessGrants.Where(item => item.Id == group.Id).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();

        var inactiveId = Guid.NewGuid();
        var inactiveKey = inactiveId.ToString("N");
        db.Users.Add(new UserEntity
        {
            Id = inactiveId, IdentityKey = inactiveKey, IdentityName = inactiveKey,
            NormalizedIdentityName = inactiveKey.ToUpperInvariant(), IsActive = false,
            CreatedAtUtc = clock.GetUtcNow(), LastSeenAtUtc = clock.GetUtcNow()
        });
        var inactiveGrantId = Guid.NewGuid();
        db.AccessGrants.Add(new AccessGrantEntity
        {
            Id = inactiveGrantId, SubjectKind = "User", UserId = inactiveId, RoleId = custom.Id,
            ScopeKind = "AllAssets", IsActive = true, Revision = 1,
            CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow()
        });
        await db.SaveChangesAsync();
        await RejectWithoutWritesAsync(() => service.MigrateAsync(true, actor));
        await db.AccessGrants.Where(item => item.Id == inactiveGrantId).ExecuteDeleteAsync();
        await db.Users.Where(item => item.Id == inactiveId).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();

        // The grant-list bound includes retired history. A new successful
        // migration must remain completely inspectable by existing APIs.
        var existing = await db.AccessGrants.CountAsync(item => item.UserId == subject.Id);
        var historical = Enumerable.Range(0, 500 - existing).Select(_ => new AccessGrantEntity
        {
            Id = Guid.NewGuid(), SubjectKind = "User", UserId = subject.Id, RoleId = custom.Id,
            ScopeKind = "AllAssets", IsActive = false, Revision = 2,
            CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow()
        }).ToArray();
        db.AccessGrants.AddRange(historical);
        await db.SaveChangesAsync();
        await RejectWithoutWritesAsync(() => service.MigrateAsync(true, actor));
        var historicalIds = historical.Select(item => item.Id).ToArray();
        await db.AccessGrants.Where(item => historicalIds.Contains(item.Id)).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();

        // Retained audit provenance counts across migration/rollback cycles.
        var syntheticProofs = Enumerable.Range(0, LegacyGrantMigrationRepository.MaximumProvenanceRecords)
            .Select(_ => new AuditEventEntity
            {
                Id = Guid.NewGuid(), OccurredAtUtc = clock.GetUtcNow(), ActorUserId = administrator.Id,
                ActorIdentity = administrator.IdentityName, Action = "security.grant.legacy-migrated",
                TargetType = "security-access-grant", TargetId = Guid.NewGuid().ToString("D")
            }).ToArray();
        db.AuditEvents.AddRange(syntheticProofs);
        await db.SaveChangesAsync();
        await RejectWithoutWritesAsync(() => service.MigrateAsync(true, actor));
        var proofIds = syntheticProofs.Select(item => item.Id).ToArray();
        await db.AuditEvents.Where(item => proofIds.Contains(item.Id)).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
        await AuditFailureAsync(false);

        // Two independent serializable writers may produce one batch only.
        await using var first = new WellSiteAutoPilotDbContext(options);
        await using var second = new WellSiteAutoPilotDbContext(options);
        var attempts = await Task.WhenAll(AttemptAsync(first), AttemptAsync(second));
        if (attempts.Count(item => item is { GrantsCreated: > 0 }) != 1)
            throw new InvalidOperationException("Concurrent migration did not create one canonical batch.");
        var winner = attempts.Single(item => item is { GrantsCreated: > 0 })!;
        if (!winner.RecordedGrantsMatchLegacy || !winner.LiveAuthorizationUnchanged)
            throw new InvalidOperationException("Migration failed its recorded parity contract.");
        var migrated = await db.AuditEvents.AsNoTracking()
            .Where(item => item.Action == "security.grant.legacy-migrated").ToArrayAsync();
        if (migrated.Length != winner.GrantsCreated || migrated.Any(item =>
            item.ActorUserId != administrator.Id || item.ActorIdentity != actor.IdentityName ||
            item.CorrelationId != actor.CorrelationId || item.OccurredAtUtc != clock.GetUtcNow()))
            throw new InvalidOperationException("Migration provenance did not atomically identify its administrator.");
        var population = await new DeploymentMigrationSnapshotRepository(db).CaptureAsync(administrator.Id);
        if (population.ActiveUsers.Any(item => !LegacyMigrationPreflightEvaluator.Evaluate(item).RecordedGrantsMatchLegacy))
            throw new InvalidOperationException("Committed migration grants did not match every active user's legacy access.");
        var auditCount = await db.AuditEvents.CountAsync();
        var grantCount = await db.AccessGrants.CountAsync();
        var replay = await service.MigrateAsync(true, actor);
        if (replay.GrantsCreated != 0 || await db.AuditEvents.CountAsync() != auditCount ||
            await db.AccessGrants.CountAsync() != grantCount)
            throw new InvalidOperationException("Migration replay duplicated grants or audits.");

        // Manual assignments are excluded from rollback provenance.
        var manual = await administration.CreateForUserAsync(subject.Id,
            new CreateAccessGrantCommand(custom.Id, AccessGrantScopeKind.ExactAssets, [assetId], custom.Revision), actor);
        await AuditFailureAsync(true);
        var migratedIds = migrated.Select(item => Guid.Parse(item.TargetId!)).ToArray();
        var tamperedId = migratedIds[0];
        var original = await db.AccessGrants.AsNoTracking().SingleAsync(item => item.Id == tamperedId);
        var changedScope = original.ScopeKind == "AllAssets" ? "DeploymentWide" : "AllAssets";
        await db.AccessGrants.Where(item => item.Id == tamperedId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ScopeKind, changedScope));
        await RejectWithoutWritesAsync(() => service.RollbackAsync(true, actor));
        await db.AccessGrants.Where(item => item.Id == tamperedId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ScopeKind, original.ScopeKind));
        var originalProof = migrated[0];
        await db.AuditEvents.Where(item => item.Id == originalProof.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DetailsJson, "{}"));
        await RejectWithoutWritesAsync(() => service.RollbackAsync(true, actor));
        await db.AuditEvents.Where(item => item.Id == originalProof.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.DetailsJson, originalProof.DetailsJson));
        var rollback = await service.RollbackAsync(true, actor);
        if (!rollback.LiveAuthorizationUnchanged || rollback.GrantsDeactivated != winner.GrantsCreated ||
            await db.AccessGrants.AnyAsync(item => migratedIds.Contains(item.Id) && (item.IsActive || item.Revision != 2)) ||
            !await db.AccessGrants.AnyAsync(item => item.Id == manual.Id && item.IsActive && item.Revision == 1) ||
            await db.AuditEvents.CountAsync(item => item.Action == "security.grant.legacy-rolled-back") != winner.GrantsCreated)
            throw new InvalidOperationException("Rollback missed migrated grants, lost audits, or changed a manual assignment.");
        auditCount = await db.AuditEvents.CountAsync();
        var repeatedRollback = await service.RollbackAsync(true, actor);
        if (repeatedRollback.GrantsDeactivated != 0 || await db.AuditEvents.CountAsync() != auditCount)
            throw new InvalidOperationException("Rollback replay changed grants or audits.");
        foreach (var profile in await users.ListAsync(101))
        {
            if (accessBefore[profile.Id] != Fingerprint(profile))
                throw new InvalidOperationException("Migration or rollback mutated live legacy authorization.");
        }
        await administration.DeactivateForUserAsync(subject.Id, manual.Id, manual.Revision, actor);
        var renewed = await service.MigrateAsync(true, actor);
        var renewedRollback = await service.RollbackAsync(true, actor);
        if (renewed.GrantsCreated != winner.GrantsCreated || renewedRollback.GrantsDeactivated != renewed.GrantsCreated ||
            renewedRollback.MigratedGrantsInspected != winner.GrantsCreated + renewed.GrantsCreated)
            throw new InvalidOperationException("Repeated migration/rollback lost retained provenance or changed its batch boundary.");

        async Task<LegacyGrantMigrationResult?> AttemptAsync(WellSiteAutoPilotDbContext context)
        {
            try { return await new LegacyGrantMigrationRepository(context).MigrateAsync(
                administrator.Id, actor.IdentityName, actor.CorrelationId, clock.GetUtcNow()); }
            catch (WellSiteAutoPilotException exception) when (exception.Code == "SECURITY_MIGRATION_CONFLICT")
            { return null; }
        }
        async Task RejectWithoutWritesAsync(Func<Task> action)
        {
            var before = await CountsAsync();
            await RejectAsync(action, "SECURITY_MIGRATION_CONFLICT");
            if (before != await CountsAsync())
                throw new InvalidOperationException("Rejected migration/rollback left a partial write.");
        }
        async Task AuditFailureAsync(bool rollback)
        {
            var before = await CountsAsync();
            var activeBefore = await db.AccessGrants.CountAsync(item => item.IsActive);
            await using var failed = new WellSiteAutoPilotDbContext(options);
            var writer = new LegacyGrantMigrationRepository(failed);
            try
            {
                if (rollback) await writer.RollbackAsync(administrator.Id, new string('x', 257), null, clock.GetUtcNow());
                else await writer.MigrateAsync(administrator.Id, new string('x', 257), null, clock.GetUtcNow());
                throw new InvalidOperationException("Migration operation returned without its durable audit.");
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "22001" }) { }
            if (before != await CountsAsync() || await db.AccessGrants.CountAsync(item => item.IsActive) != activeBefore)
                throw new InvalidOperationException("Audit failure left partial migration/rollback state.");
        }
        async Task<(int Grants, int Bindings, int Audits)> CountsAsync() =>
            (await db.AccessGrants.CountAsync(), await db.AccessGrantAssets.CountAsync(), await db.AuditEvents.CountAsync());
    }
    private static string Fingerprint(UserAccessProfile profile) => JsonSerializer.Serialize(new
    { profile.IsActive, Roles = profile.Roles.OrderBy(role => role), Assets = profile.AssetScopeIds.OrderBy(id => id) });
    private static async Task RejectAsync(Func<Task> action, string code)
    {
        try { await action(); throw new InvalidOperationException($"Unsafe migration operation accepted: {code}."); }
        catch (WellSiteAutoPilotException exception) when (exception.Code == code) { }
    }
}
