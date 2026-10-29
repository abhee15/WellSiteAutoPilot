using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Recommendations;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.Audit;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Recommendations;
using WellSiteAutoPilot.Persistence.Security;

internal static class AuthorizedRecommendationChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid assetTypeId)
    {
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var user = Guid.NewGuid();
        var identity = "decision-" + user.ToString("N");
        var decideRole = Guid.NewGuid();
        var readRole = Guid.NewGuid();
        var decideGrant = Guid.NewGuid();
        var readGrant = Guid.NewGuid();
        var assetA = Guid.NewGuid();
        var assetB = Guid.NewGuid();
        var config = Guid.NewGuid();
        var revision = Guid.NewGuid();
        var execution = Guid.NewGuid();
        var recommendations = new List<Guid>();
        setup.Users.Add(new UserEntity
        {
            Id = user, IdentityKey = identity, IdentityName = identity, NormalizedIdentityName = identity,
            IsActive = true, CreatedAtUtc = now, LastSeenAtUtc = now
        });
        foreach (var (id, permission) in new[]
        { (decideRole, WellSitePermissions.RecommendationsDecide), (readRole, WellSitePermissions.RecommendationsRead) })
        {
            setup.RoleDefinitions.Add(new RoleDefinitionEntity
            {
                Id = id, Key = id.ToString("N"), Name = id.ToString("N"), NormalizedName = id.ToString("N"),
                IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
            });
            setup.RolePermissions.Add(new RolePermissionEntity { RoleId = id, Permission = permission });
        }
        foreach (var id in new[] { assetA, assetB })
            setup.Assets.Add(new AssetEntity { Id = id, AssetTypeId = assetTypeId, Name = identity, IsActive = true, CreatedAtUtc = now });
        setup.AccessGrants.AddRange(new AccessGrantEntity
        {
            Id = decideGrant, UserId = user, RoleId = decideRole, ScopeKind = "ExactAssets", IsActive = true,
            Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
        }, new AccessGrantEntity
        {
            Id = readGrant, SubjectKind = "Group", GroupId = "S-1-5-21-912-1", RoleId = readRole,
            ScopeKind = "ExactAssets", IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
        });
        setup.AccessGrantAssets.AddRange(new AccessGrantAssetEntity { GrantId = decideGrant, AssetId = assetA },
            new AccessGrantAssetEntity { GrantId = readGrant, AssetId = assetB });
        setup.ConfiguredLogicDefinitions.Add(new ConfiguredLogicEntity
        { Id = config, Name = identity, ActiveRevisionId = revision, CreatedAtUtc = now });
        setup.ConfiguredLogicRevisions.Add(new ConfiguredLogicRevisionEntity
        {
            Id = revision, ConfiguredLogicId = config, RevisionNumber = 1, ModuleId = "decision-test",
            ModuleVersion = "1.0.0", Mode = "Recommendation", Status = "Active", CreatedAtUtc = now
        });
        setup.ConfiguredLogicAssetBindings.Add(new ConfiguredLogicAssetBindingEntity
        { RevisionId = revision, Role = "target", AssetId = assetA });
        setup.Executions.Add(new ExecutionEntity
        {
            Id = execution, LogicInstanceId = config, ConfigurationRevisionId = revision, ModuleId = "decision-test",
            ModuleVersion = "1.0.0", Mode = "Recommendation", Status = "Completed", RequestContractVersion = 2,
            Trigger = "Manual", RequestedAtUtc = now
        });
        setup.ExecutionAssetScopes.AddRange(new ExecutionAssetScopeEntity { ExecutionId = execution, LogicInstanceId = config, AssetId = assetA },
            new ExecutionAssetScopeEntity { ExecutionId = execution, LogicInstanceId = config, AssetId = assetB });
        var allowed = AddRecommendation(assetA);
        var forbidden = AddRecommendation(assetB);
        var expired = AddRecommendation(assetA, now.AddMinutes(-1));
        var rollback = AddRecommendation(assetA);
        var concurrent = AddRecommendation(assetA);
        await setup.SaveChangesAsync();
        var subject = new AccessSubject(user, new VerifiedWindowsGroupMembership(user, ["S-1-5-21-912-1"], now));
        var actor = new SecurityActorContext(user, identity, "grant-decision-test");
        var repository = new RecommendationRepository(setup);
        if (await repository.GetAuthorizedAsync(subject, allowed) is not null ||
            (await repository.GetAuthorizedAsync(subject, forbidden))?.Id != forbidden)
            throw new InvalidOperationException("Read and decision capabilities were implicitly combined.");
        await ExpectFailureAsync(() => DecideAsync(forbidden), FailureKind.Authorization);
        await ExpectFailureAsync(() => DecideAsync(expired), FailureKind.Conflict);
        if (await setup.AuditEvents.AnyAsync(audit => audit.ActorUserId == user))
            throw new InvalidOperationException("Denied/stale recommendation wrote an audit mutation.");
        var approved = await DecideAsync(allowed);
        var audit = await setup.AuditEvents.SingleAsync(item => item.ActorUserId == user);
        if (approved.Status != RecommendationStatus.Approved || audit.AssetId != assetA || audit.CorrelationId != actor.CorrelationId)
            throw new InvalidOperationException("Authorized decision lost its target, actor or atomic audit.");
        await ExpectFailureAsync(() => DecideAsync(allowed), FailureKind.Conflict);

        await setup.Executions.Where(item => item.Id == execution)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConfigurationRevisionId, Guid.NewGuid()));
        await ExpectFailureAsync(() => DecideAsync(rollback), FailureKind.Conflict);
        await setup.Executions.Where(item => item.Id == execution)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConfigurationRevisionId, revision));
        await setup.ExecutionAssetScopes.Where(item => item.ExecutionId == execution && item.AssetId == assetA)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LogicInstanceId, Guid.NewGuid()));
        await ExpectFailureAsync(() => DecideAsync(rollback), FailureKind.Conflict);
        await setup.ExecutionAssetScopes.Where(item => item.ExecutionId == execution && item.AssetId == assetA)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.LogicInstanceId, config));
        await setup.ConfiguredLogicDefinitions.Where(item => item.Id == config)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ActiveRevisionId, (Guid?)Guid.NewGuid()));
        await ExpectFailureAsync(() => DecideAsync(rollback), FailureKind.Conflict);
        await setup.ConfiguredLogicDefinitions.Where(item => item.Id == config)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ActiveRevisionId, (Guid?)revision));

        var failureOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(new RejectAudit()).Options;
        await using (var failing = new WellSiteAutoPilotDbContext(failureOptions))
        {
            try
            {
                await new RecommendationRepository(failing).DecideAuthorizedAsync(subject, rollback, RecommendationStatus.Rejected, actor, null);
                throw new InvalidOperationException("Decision ignored audit failure.");
            }
            catch (InvalidOperationException exception) when (exception.Message == "Injected decision audit failure.") { }
        }
        if ((await setup.Recommendations.AsNoTracking().SingleAsync(item => item.Id == rollback)).Status != "Pending" ||
            await setup.AuditEvents.CountAsync(item => item.ActorUserId == user) != 1)
            throw new InvalidOperationException("Decision/audit did not roll back together.");

        var pause = new PauseAudit();
        var pausedOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(pause).Options;
        await using var writer = new WellSiteAutoPilotDbContext(pausedOptions);
        var mutation = new RecommendationRepository(writer).DecideAuthorizedAsync(subject, concurrent, RecommendationStatus.Rejected, actor, null);
        await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var connection = new NpgsqlConnectionStringBuilder(setup.Database.GetConnectionString()!)
        { ApplicationName = "wsa-recommendation-revoke-" + Guid.NewGuid().ToString("N"), CommandTimeout = 15 };
        await using var revoker = new NpgsqlConnection(connection.ConnectionString);
        await revoker.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE security.access_grants SET \"IsActive\" = FALSE WHERE \"Id\" = $1", revoker);
        command.Parameters.AddWithValue(decideGrant);
        var revocation = command.ExecuteNonQueryAsync();
        try
        {
            await using var observer = new NpgsqlConnection(setup.Database.GetConnectionString());
            await observer.OpenAsync();
            await using var waiting = new NpgsqlCommand("""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = $1
                AND wait_event_type = 'Lock' AND cardinality(pg_blocking_pids(pid)) > 0)
                """, observer);
            waiting.Parameters.AddWithValue(connection.ApplicationName!);
            var timer = Stopwatch.StartNew();
            while (!Equals(await waiting.ExecuteScalarAsync(), true))
            {
                if (revocation.IsCompleted || timer.Elapsed > TimeSpan.FromSeconds(8))
                    throw new InvalidOperationException("Recommendation decision did not retain grant authority until commit.");
                await Task.Delay(20);
            }
        }
        finally { pause.Release.TrySetResult(); }
        if ((await mutation).Status != RecommendationStatus.Rejected || await revocation != 1)
            throw new InvalidOperationException("Authorized decision/revocation commit order was lost.");
        await ExpectFailureAsync(() => DecideAsync(rollback), FailureKind.Authorization);
        if (await setup.AuditEvents.CountAsync(item => item.ActorUserId == user) != 2)
            throw new InvalidOperationException("Post-revocation decision left an audit mutation.");
        await setup.AccessGrants.Where(item => item.Id == decideGrant)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.IsActive, true));
        await setup.Executions.Where(item => item.Id == execution)
            .ExecuteUpdateAsync(set => set.SetProperty(item => item.RequestContractVersion, 3));
        var hostedRecommendation = AddRecommendation(assetA);
        await setup.SaveChangesAsync();
        if ((await DecideAsync(hostedRecommendation)).Status != RecommendationStatus.Approved)
            throw new InvalidOperationException("Hosted execution recommendation lost its decision path.");
        await ExpectFailureAsync(() => DecideAsync(forbidden), FailureKind.Authorization);
        await setup.AuditEvents.Where(item => item.ActorUserId == user).ExecuteDeleteAsync();
        await setup.Recommendations.Where(item => recommendations.Contains(item.Id)).ExecuteDeleteAsync();
        await setup.Executions.Where(item => item.Id == execution).ExecuteDeleteAsync();
        await setup.ConfiguredLogicDefinitions.Where(item => item.Id == config).ExecuteDeleteAsync();
        await setup.AccessGrants.Where(item => item.Id == decideGrant || item.Id == readGrant).ExecuteDeleteAsync();
        await setup.RolePermissions.Where(item => item.RoleId == decideRole || item.RoleId == readRole).ExecuteDeleteAsync();
        await setup.RoleDefinitions.Where(item => item.Id == decideRole || item.Id == readRole).ExecuteDeleteAsync();
        await setup.Assets.Where(item => item.Id == assetA || item.Id == assetB).ExecuteDeleteAsync();
        await setup.Users.Where(item => item.Id == user).ExecuteDeleteAsync();
        Console.WriteLine("Grant-authorized recommendation decisions: separate read/decide scope, stale/duplicate refusal, atomic audit rollback and actual revocation lock wait passed.");

        async Task<RecommendationRecord> DecideAsync(Guid id)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            return await new RecommendationRepository(db).DecideAuthorizedAsync(subject, id, RecommendationStatus.Approved, actor, null);
        }
        Guid AddRecommendation(Guid asset, DateTimeOffset? expires = null)
        {
            var id = Guid.NewGuid();
            recommendations.Add(id);
            setup.Recommendations.Add(new RecommendationEntity
            {
                Id = id, ExecutionId = execution, IntentIndex = recommendations.Count, ConfiguredLogicId = config,
                ConfigurationRevisionId = revision, ModuleId = "decision-test", ModuleVersion = "1.0.0", AssetId = asset,
                Code = "TEST", Command = "test", ReasonCode = "TEST", Status = "Pending", CreatedAtUtc = now, ExpiresAtUtc = expires
            });
            return id;
        }
    }

    private static async Task ExpectFailureAsync(Func<Task<RecommendationRecord>> action, FailureKind kind)
    {
        try { await action(); throw new InvalidOperationException("Invalid recommendation decision succeeded."); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == kind) { }
    }

    private sealed class RejectAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected decision audit failure.");
    }

    private sealed class PauseAudit : SaveChangesInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }
}
