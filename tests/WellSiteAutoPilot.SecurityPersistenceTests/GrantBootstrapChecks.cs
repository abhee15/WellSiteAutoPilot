using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.Security;

internal static class GrantBootstrapChecks
{
    public static async Task RunAsync(string connectionString)
    {
        // Bootstrap requires an actually fresh deployment. This isolated,
        // disposable database also proves AllAssets hints with zero Assets.
        var database = "wsa_grant_bootstrap_" + Guid.NewGuid().ToString("N");
        await using var owner = new NpgsqlConnection(connectionString);
        await owner.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", owner))
            await create.ExecuteNonQueryAsync();
        try
        {
            var isolated = new NpgsqlConnectionStringBuilder(connectionString) { Database = database };
            var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>()
                .UseNpgsql(isolated.ConnectionString).Options;
            await using var setup = new WellSiteAutoPilotDbContext(options);
            await setup.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            var first = await AddUserAsync();
            var second = await AddUserAsync();
            var inactive = await AddUserAsync(false);
            if (await new GrantBootstrapRepository(setup).TryInitializeAdministratorAsync(inactive, now) ||
                await new GrantBootstrapRepository(setup).TryInitializeAdministratorAsync(Guid.NewGuid(), now))
                throw new InvalidOperationException("Unknown/inactive identity initialized administrator grants.");

            var initialized = await Task.WhenAll(InitializeAsync(first), InitializeAsync(second));
            if (initialized.Count(value => value) != 1 || await setup.AccessGrants.CountAsync() != 2 ||
                await setup.AuditEvents.CountAsync(audit => audit.Action == GrantBootstrapRepository.AuditAction) != 1 ||
                await setup.UserRoles.AnyAsync() || await setup.UserAssetScopes.AnyAsync() || await setup.AccessGrantAssets.AnyAsync())
                throw new InvalidOperationException("Concurrent bootstrap did not create exactly one atomic grant pair/audit.");
            var winner = initialized[0] ? first : second;
            var grants = await setup.AccessGrants.AsNoTracking().ToArrayAsync();
            if (grants.Any(grant => grant.UserId != winner || grant.SubjectKind != "User" || grant.GroupSid is not null ||
                    !grant.IsActive || grant.Revision != 1) ||
                !grants.Select(grant => grant.ScopeKind).ToHashSet().SetEquals(["DeploymentWide", "AllAssets"]))
                throw new InvalidOperationException("Bootstrap recovery/Asset assignments were malformed.");
            var hints = await new RequestAccessRepository(setup, TimeProvider.System)
                .GetPermissionHintsAsync(new AccessSubject(winner));
            if (!hints.ToHashSet().SetEquals(WellSitePermissions.All) || await setup.Assets.AnyAsync())
                throw new InvalidOperationException("Fresh-deployment bootstrap lost coarse capabilities before the first Asset.");
            if (await InitializeAsync(first) || await InitializeAsync(second))
                throw new InvalidOperationException("Bootstrap was repeated for an initialized deployment.");
            await setup.AccessGrants.ExecuteUpdateAsync(setters => setters.SetProperty(grant => grant.IsActive, false));
            if (await InitializeAsync(second)) throw new InvalidOperationException("Bootstrap restored retired authority.");
            await setup.AccessGrants.ExecuteDeleteAsync();
            if (await InitializeAsync(second)) throw new InvalidOperationException("Bootstrap ignored its durable audit marker.");

            await setup.AuditEvents.ExecuteDeleteAsync();
            var failingOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options)
                .AddInterceptors(new RejectBootstrapAudit()).Options;
            await using (var failing = new WellSiteAutoPilotDbContext(failingOptions))
            {
                try
                {
                    await new GrantBootstrapRepository(failing).TryInitializeAdministratorAsync(first, now);
                    throw new InvalidOperationException("Bootstrap ignored audit failure.");
                }
                catch (InvalidOperationException exception) when (exception.Message == "Injected bootstrap audit failure.") { }
            }
            if (await setup.AccessGrants.AnyAsync() || await setup.AuditEvents.AnyAsync())
                throw new InvalidOperationException("Failed bootstrap left partial authority/audit.");

            await using (var invalid = new WellSiteAutoPilotDbContext(options))
            {
                await using var transaction = await invalid.Database.BeginTransactionAsync();
                // The repository owns its transaction, so commit this corrupt
                // fixture and restore it before the following independent case.
                await invalid.RoleDefinitions.Where(role => role.Key == "admin")
                    .ExecuteUpdateAsync(setters => setters.SetProperty(role => role.IsActive, false));
                await transaction.CommitAsync();
            }
            await ExpectInvalidRoleAsync();
            await setup.RoleDefinitions.Where(role => role.Key == "admin")
                .ExecuteUpdateAsync(setters => setters.SetProperty(role => role.IsActive, true));
            var adminId = await setup.RoleDefinitions.Where(role => role.Key == "admin").Select(role => role.Id).SingleAsync();
            await setup.RolePermissions.Where(item => item.RoleId == adminId && item.Permission == WellSitePermissions.SecurityManage)
                .ExecuteDeleteAsync();
            await ExpectInvalidRoleAsync();
            setup.RolePermissions.Add(new RolePermissionEntity { RoleId = adminId, Permission = WellSitePermissions.SecurityManage });
            await setup.SaveChangesAsync();
            if (!await InitializeAsync(first)) throw new InvalidOperationException("A repaired fresh deployment could not initialize.");

            async Task<Guid> AddUserAsync(bool active = true)
            {
                var id = Guid.NewGuid();
                var identity = "bootstrap-" + id.ToString("N");
                setup.Users.Add(new UserEntity
                {
                    Id = id, IdentityKey = identity, IdentityName = identity, NormalizedIdentityName = identity,
                    IsActive = active, CreatedAtUtc = now, LastSeenAtUtc = now
                });
                await setup.SaveChangesAsync();
                return id;
            }
            async Task<bool> InitializeAsync(Guid userId)
            {
                await using var db = new WellSiteAutoPilotDbContext(options);
                return await new GrantBootstrapRepository(db).TryInitializeAdministratorAsync(userId, now);
            }
            async Task ExpectInvalidRoleAsync()
            {
                try { await InitializeAsync(first); throw new InvalidOperationException("Invalid protected role initialized grants."); }
                catch (WellSiteAutoPilotException exception) when (exception.Code == "SECURITY_BOOTSTRAP_ROLE_INVALID") { }
                if (await setup.AccessGrants.AnyAsync() || await setup.AuditEvents.AnyAsync())
                    throw new InvalidOperationException("Invalid bootstrap role left authority/audit.");
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", owner);
            await drop.ExecuteNonQueryAsync();
        }
        Console.WriteLine("Fresh-database grant bootstrap: concurrent single winner, separate direct recovery/AllAssets grants, empty deployment hints, durable marker, invalid role and atomic audit failure checks passed.");
    }

    private sealed class RejectBootstrapAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditEventEntity>().Any(entry =>
                    entry.State == EntityState.Added && entry.Entity.Action == GrantBootstrapRepository.AuditAction))
                throw new InvalidOperationException("Injected bootstrap audit failure.");
            return ValueTask.FromResult(result);
        }
    }
}
