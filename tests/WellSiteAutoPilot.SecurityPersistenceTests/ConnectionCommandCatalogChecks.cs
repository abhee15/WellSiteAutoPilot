using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Connections;
using WellSiteAutoPilot.Application.Control;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Connections;
using WellSiteAutoPilot.Persistence.Security;

/// <summary>
/// The command catalog is the hard ceiling on what a site can be commanded to do, so saving one needs both the control
/// and the connection right, quotes the stored version, and leaves a history entry and an audit event together.
/// </summary>
internal static class ConnectionCommandCatalogChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options)
    {
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var user = Guid.NewGuid();
        var identity = "catalog-" + user.ToString("N");
        var connection = Guid.NewGuid();
        var controlRole = Guid.NewGuid();
        var connectionRole = Guid.NewGuid();
        var controlGrant = Guid.NewGuid();
        var connectionGrant = Guid.NewGuid();
        setup.Users.Add(new UserEntity
        {
            Id = user, IdentityKey = identity, IdentityName = identity, NormalizedIdentityName = identity,
            IsActive = true, CreatedAtUtc = now, LastSeenAtUtc = now
        });
        foreach (var (role, permission, grant) in new[]
        { (controlRole, WellSitePermissions.ControlManage, controlGrant), (connectionRole, WellSitePermissions.ConnectionsManage, connectionGrant) })
        {
            setup.RoleDefinitions.Add(new RoleDefinitionEntity
            {
                Id = role, Key = role.ToString("N"), Name = role.ToString("N"), NormalizedName = role.ToString("N"),
                IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            });
            setup.RolePermissions.Add(new RolePermissionEntity { RoleId = role, Permission = permission });
            setup.AccessGrants.Add(new AccessGrantEntity
            {
                Id = grant, SubjectKind = "User", UserId = user, RoleId = role, ScopeKind = "DeploymentWide",
                IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            });
        }

        setup.Connections.Add(new ConnectionEntity
        {
            Id = connection, Name = identity, NormalizedName = identity.ToUpperInvariant(), AdapterKey = "simulator",
            SettingsVersion = 1, IsEnabled = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
        });
        await setup.SaveChangesAsync();

        var subject = new AccessSubject(user);
        var actor = new SecurityActorContext(user, identity, "catalog-" + user.ToString("N"));

        ConnectionCommandCatalogEntry Entry(string command, bool enabled = true, decimal? max = 60) =>
            new(command, enabled, enabled ? 30 : null, enabled ? max : null, enabled ? 5 : null,
                new Dictionary<string, string> { ["setpoint"] = "SP1" }, 5, 30);

        async Task<ConnectionCommandCatalog> ReplaceAsync(
            int expectedVersion, params ConnectionCommandCatalogEntry[] entries)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            return await new ConnectionCommandCatalogRepository(db)
                .ReplaceAuthorizedAsync(connection, expectedVersion, entries, "  commissioning  ", subject, actor);
        }

        async Task ExpectAsync(Func<Task> operation, FailureKind kind, string what)
        {
            try { await operation(); }
            catch (WellSiteAutoPilotException exception) when (exception.Kind == kind) { return; }
            throw new InvalidOperationException($"Expected {kind}: {what}.");
        }

        // Either right alone is not enough, and nothing is left behind by a refusal.
        foreach (var grant in new[] { controlGrant, connectionGrant })
        {
            await SetGrantAsync(grant, false);
            try { await ExpectAsync(() => ReplaceAsync(0, Entry("SET_SPEED")), FailureKind.Authorization, "a missing right"); }
            finally { await SetGrantAsync(grant, true); }
        }

        await using (var mismatched = new WellSiteAutoPilotDbContext(options))
            await ExpectAsync(() => new ConnectionCommandCatalogRepository(mismatched)
                .ReplaceAuthorizedAsync(connection, 0, [], null, new AccessSubject(Guid.NewGuid()), actor), FailureKind.Authorization,
                "an actor that is not the subject");
        if (await CatalogAsync() is not null || await AuditCountAsync() != 0)
            throw new InvalidOperationException("A refused catalog change left state or an audit entry behind.");

        await ExpectAsync(() => ReplaceAsync(1, Entry("SET_SPEED")), FailureKind.Conflict, "a version that was never stored");
        await using (var unknown = new WellSiteAutoPilotDbContext(options))
            await ExpectAsync(() => new ConnectionCommandCatalogRepository(unknown)
                .ReplaceAuthorizedAsync(Guid.NewGuid(), 0, [], null, subject, actor), FailureKind.NotFound, "an unknown connection");

        var first = await ReplaceAsync(0, Entry("SET_SPEED"), Entry("PUMP", enabled: false));
        if (first.Version != 1 || first.ChangedBy != identity || first.Reason != "commissioning" || first.Entries.Count != 2 ||
            first.Entries[0].Command != "PUMP" || first.Entries[0].Enabled || first.Entries[1].Command != "SET_SPEED" ||
            first.Entries[1].Maximum != 60 || first.Entries[1].Target["setpoint"] != "SP1")
            throw new InvalidOperationException("The first catalog must be stored in order with the authenticated author and a trimmed reason.");

        await ExpectAsync(() => ReplaceAsync(0, Entry("SET_SPEED")), FailureKind.Conflict, "a stale version");
        await ExpectAsync(() => ReplaceAsync(1, Entry("SET_SPEED"), Entry("set_speed")), FailureKind.Validation, "a repeated command");
        var second = await ReplaceAsync(1, Entry("SET_SPEED", max: 50));
        if (second.Version != 2 || second.Entries.Count != 1 || second.Entries[0].Maximum != 50)
            throw new InvalidOperationException("Replacing the catalog must replace every entry, not merge.");

        var changes = await setup.ConnectionCommandCatalogChanges.Where(item => item.ConnectionId == connection)
            .OrderBy(item => item.Version).ToArrayAsync();
        if (changes.Length != 2 || changes[0].Version != 1 || changes[1].Version != 2 ||
            JsonDocument.Parse(changes[0].SnapshotJson).RootElement.GetArrayLength() != 2 ||
            JsonDocument.Parse(changes[1].SnapshotJson).RootElement.GetArrayLength() != 1 ||
            changes.Any(item => item.ChangedBy != identity))
            throw new InvalidOperationException("Every change must keep the whole catalog it produced.");
        if (await AuditCountAsync() != 2)
            throw new InvalidOperationException("Each successful catalog change must be audited once.");

        // The database refuses an enabled command without limits, whatever wrote it.
        await using (var raw = new WellSiteAutoPilotDbContext(options))
        {
            raw.ConnectionCommandCatalogEntries.Add(new ConnectionCommandCatalogEntryEntity
            {
                ConnectionId = connection, Command = "RAW", Enabled = true, TargetJson = "{}", SettleSeconds = 1, DeadlineSeconds = 5
            });
            var refused = false;
            try { await raw.SaveChangesAsync(); }
            catch (DbUpdateException) { refused = true; }
            if (!refused) throw new InvalidOperationException("The database must refuse an enabled command without limits.");
        }

        // A connection with a catalog keeps its history, so it cannot be deleted.
        await using (var delete = new WellSiteAutoPilotDbContext(options))
            await ExpectAsync(() => new ConnectionRepository(delete).DeleteAuthorizedAsync(connection, subject, actor),
                FailureKind.Conflict, "deleting a connection that has a catalog");

        async Task SetGrantAsync(Guid grant, bool active)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            await db.AccessGrants.Where(item => item.Id == grant).ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, active));
        }

        async Task<ConnectionCommandCatalogEntity?> CatalogAsync()
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            return await db.ConnectionCommandCatalogs.AsNoTracking().SingleOrDefaultAsync(item => item.ConnectionId == connection);
        }

        async Task<int> AuditCountAsync()
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            return await db.AuditEvents.CountAsync(item =>
                item.TargetId == connection.ToString() && item.Action == "connection.command-catalog.changed");
        }
    }
}
