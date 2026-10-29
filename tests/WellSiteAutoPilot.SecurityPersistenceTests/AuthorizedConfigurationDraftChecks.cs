using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Diagnostics;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Application.ConfiguredLogic;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Domain.ConfiguredLogic;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Logic;
using WellSiteAutoPilot.Persistence.Security;

internal static class AuthorizedConfigurationDraftChecks
{
    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid assetTypeId)
    {
        foreach (var group in new[] { false, true })
        {
            var now = DateTimeOffset.UtcNow;
            var user = Guid.NewGuid();
            var role = Guid.NewGuid();
            var readRole = Guid.NewGuid();
            var grant = Guid.NewGuid();
            var readGrant = Guid.NewGuid();
            var assetA = Guid.NewGuid();
            var assetB = Guid.NewGuid();
            var sid = "S-1-5-21-909-812";
            var name = "draft-" + user.ToString("N");
            await using var setup = new WellSiteAutoPilotDbContext(options);
            setup.Users.Add(new UserEntity
            {
                Id = user, IdentityKey = name, IdentityName = name, NormalizedIdentityName = name,
                IsActive = true, CreatedAtUtc = now, LastSeenAtUtc = now
            });
            foreach (var id in new[] { role, readRole })
                setup.RoleDefinitions.Add(new RoleDefinitionEntity
                {
                    Id = id, Key = id.ToString("N"), Name = id.ToString("N"), NormalizedName = id.ToString("N"),
                    IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
                });
            setup.RolePermissions.AddRange(
                new RolePermissionEntity { RoleId = role, Permission = WellSitePermissions.ConfiguredLogicManage },
                new RolePermissionEntity { RoleId = readRole, Permission = WellSitePermissions.ConfiguredLogicRead });
            setup.AccessGrants.AddRange(
                new AccessGrantEntity
                {
                    Id = grant, RoleId = role, SubjectKind = group ? "Group" : "User",
                    UserId = group ? null : user, GroupId = group ? sid : null,
                    ScopeKind = "ExactAssets", IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
                },
                new AccessGrantEntity
                {
                    Id = readGrant, RoleId = readRole, UserId = user,
                    ScopeKind = "ExactAssets", IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now
                });
            foreach (var id in new[] { assetA, assetB })
                setup.Assets.Add(new AssetEntity { Id = id, AssetTypeId = assetTypeId, Name = name, IsActive = true, CreatedAtUtc = now });
            setup.AccessGrantAssets.AddRange(
                new AccessGrantAssetEntity { GrantId = grant, AssetId = assetA },
                new AccessGrantAssetEntity { GrantId = readGrant, AssetId = assetA });
            await setup.SaveChangesAsync();
            var subject = new AccessSubject(user, group ? new VerifiedWindowsGroupMembership(user, [sid], now) : null);
            var actor = new SecurityActorContext(user, name, "draft-probe");
            var typeKey = await setup.AssetTypes.Where(item => item.Id == assetTypeId).Select(item => item.Key).SingleAsync();
            var manifest = $$"""
                {"manifestVersion":1,"moduleId":"{{name}}","version":"1.0.0","displayName":"Draft probe",
                 "publisher":"Test","runtime":"DotNet","executionProfile":"ScheduledOneShot",
                 "entryPoint":"Test.EntryPoint","minimumSdkVersion":"1.0.0",
                 "assetRequirements":[{"role":"target","minimumCount":1,"maximumCount":1,"requiredAssetTypeKey":"{{typeKey}}","requiredTraits":[]}],
                 "dataRequirements":[],"commandRequirements":[]}
                """;
            var catalog = new LogicModuleCatalogRepository(setup);
            await new LogicModuleCatalogService(catalog, TimeProvider.System).RegisterAsync(
                new(manifest, new string('1', 64), LogicModuleTrustStatus.TrustedPublisher));
            var service = new ConfiguredLogicService(new ConfiguredLogicRepository(setup), new AssetRepository(setup), catalog, TimeProvider.System);
            var draftCommand = new CreateConfiguredLogicCommand(name, manifest, "{}", [new("target", assetA, "{}")], [], null);
            var applicationDraft = await service.CreateAuthorizedAsync(draftCommand, subject, actor);
            await service.CreateRevisionAuthorizedAsync(applicationDraft.Id,
                new(manifest, "{}", draftCommand.AssetBindings, [], null), subject, actor);
            await ExpectAsync(() => service.CreateAuthorizedAsync(draftCommand with { ParametersJson = "[]" }, subject, actor), FailureKind.Validation);
            var lifecycleAuditCount = await AuthorizedConfigurationLifecycleChecks.RunAsync(options, applicationDraft.Id,
                subject, actor, grant, readGrant, assetB, assetTypeId, name);
            var requestAuditCount = await AuthorizedManualShadowRequestChecks.RunAsync(options, applicationDraft.Id,
                subject, actor, role, grant, readGrant, assetB, name);
            var catalogAuditCount = await AuthorizedModuleCatalogChecks.RunAsync(options, subject, actor, role, manifest);
            var config = Guid.NewGuid();
            var first = Draft(config, 1, assetA);
            await WriteAsync(repository => repository.AddAuthorizedAsync(Definition(first), subject, actor));
            await WriteAsync(repository => repository.AddRevisionAuthorizedAsync(Draft(config, 2, assetA), subject, actor));
            var failOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options)
                .AddInterceptors(new FailAfterSave()).Options;
            await using (var failing = new WellSiteAutoPilotDbContext(failOptions))
            {
                try
                {
                    await new ConfiguredLogicRepository(failing).AddRevisionAuthorizedAsync(Draft(config, 3, assetA), subject, actor);
                    throw new InvalidOperationException("Injected failure was not observed.");
                }
                catch (InvalidOperationException exception) when (exception.Message == "draft-save-probe") { }
            }
            await ExpectAsync(() => WriteAsync(repository => repository.AddRevisionAuthorizedAsync(
                Draft(config, 2, assetA), subject, actor)), FailureKind.Conflict);
            await ExpectAsync(() => WriteAsync(repository => repository.AddRevisionAuthorizedAsync(
                Draft(config, 3, assetB), subject, actor)), FailureKind.Authorization);
            // Data inputs participate even when the target binding is authorized.
            var inputOutside = Draft(config, 3, assetA) with
            {
                DataBindings = [new("input", assetB, "test", "external", "{}")]
            };
            await ExpectAsync(() => WriteAsync(repository => repository.AddRevisionAuthorizedAsync(inputOutside, subject, actor)), FailureKind.Authorization);
            // A foreign persisted configuration cannot be taken over by sending
            // a replacement revision whose incoming target is in the user's scope.
            var foreign = Draft(Guid.NewGuid(), 1, assetB);
            await WriteAsync(repository => repository.AddAsync(Definition(foreign)));
            await ExpectAsync(() => WriteAsync(repository => repository.AddRevisionAuthorizedAsync(
                Draft(foreign.ConfiguredLogicId, 2, assetA), subject, actor)), FailureKind.Authorization);
            await ExpectAsync(() => WriteAsync(repository => repository.AddRevisionAuthorizedAsync(
                Draft(config, 3, assetA), subject, actor with { UserId = Guid.NewGuid() })), FailureKind.Authorization);
            if (group)
                await ExpectAsync(() => WriteAsync(repository => repository.AddRevisionAuthorizedAsync(
                    Draft(config, 3, assetA), new AccessSubject(user), actor)), FailureKind.Authorization);
            var pause = new PauseAfterSave();
            var writerOptions = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>(options).AddInterceptors(pause).Options;
            await using (var writer = new WellSiteAutoPilotDbContext(writerOptions))
            {
                var write = new ConfiguredLogicRepository(writer).AddRevisionAuthorizedAsync(Draft(config, 3, assetA), subject, actor);
                try
                {
                    await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    var connection = new NpgsqlConnectionStringBuilder(setup.Database.GetConnectionString()!)
                    { ApplicationName = "draft-revoke-" + user.ToString("N"), CommandTimeout = 15 };
                    await using var revoker = new NpgsqlConnection(connection.ConnectionString);
                    await revoker.OpenAsync();
                    await using var command = new NpgsqlCommand("UPDATE security.access_grants SET \"IsActive\" = FALSE WHERE \"Id\" = $1", revoker);
                    command.Parameters.AddWithValue(grant);
                    var revoke = command.ExecuteNonQueryAsync();
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
                            if (revoke.IsCompleted || timer.Elapsed > TimeSpan.FromSeconds(8))
                                throw new InvalidOperationException("Configuration draft did not retain grant authority through commit.");
                            await Task.Delay(20);
                        }
                    }
                    finally { pause.Release.TrySetResult(); }
                    await write;
                    if (await revoke != 1) throw new InvalidOperationException("Draft revocation did not complete.");
                }
                finally
                {
                    pause.Release.TrySetResult();
                    await write;
                }
            }
            await ExpectAsync(() => WriteAsync(repository => repository.AddRevisionAuthorizedAsync(
                Draft(config, 4, assetA), subject, actor)), FailureKind.Authorization);
            if (await setup.ConfiguredLogicRevisions.CountAsync(item => item.ConfiguredLogicId == config) != 3 ||
                await setup.ConfiguredLogicRevisions.CountAsync(item => item.ConfiguredLogicId == foreign.ConfiguredLogicId) != 1 ||
                await setup.AuditEvents.CountAsync(item => item.CorrelationId == actor.CorrelationId && item.ActorUserId == user) != 5 + lifecycleAuditCount + requestAuditCount + catalogAuditCount)
                throw new InvalidOperationException("A denied draft write changed persisted configuration or audit state.");
            await setup.ConfiguredLogicDefinitions.Where(item => item.Id == config || item.Id == foreign.ConfiguredLogicId || item.Id == applicationDraft.Id).ExecuteDeleteAsync();
            await setup.LogicModules.Where(item => item.ModuleId == name).ExecuteDeleteAsync();
            await setup.AuditEvents.Where(item => item.ActorUserId == user).ExecuteDeleteAsync();
            await setup.AccessGrants.Where(item => item.Id == grant || item.Id == readGrant).ExecuteDeleteAsync();
            await setup.RolePermissions.Where(item => item.RoleId == role || item.RoleId == readRole).ExecuteDeleteAsync();
            await setup.RoleDefinitions.Where(item => item.Id == role || item.Id == readRole).ExecuteDeleteAsync();
            await setup.Assets.Where(item => item.Id == assetA || item.Id == assetB).ExecuteDeleteAsync();
            await setup.Users.Where(item => item.Id == user).ExecuteDeleteAsync();

            async Task WriteAsync(Func<ConfiguredLogicRepository, Task> write)
            {
                await using var db = new WellSiteAutoPilotDbContext(options);
                await write(new ConfiguredLogicRepository(db));
            }
            ConfiguredLogicRevision Draft(Guid id, int number, Guid asset) => new(
                Guid.NewGuid(), id, number, "draft-probe", "1.0.0", "{}", ExecutionMode.Shadow, "{}",
                ConfiguredLogicRevisionStatus.Draft, now, null, null, null, [new("target", asset, "{}")], []);
            ConfiguredLogicDefinition Definition(ConfiguredLogicRevision revision) => new(
                revision.ConfiguredLogicId, name, null, now, [revision]);
        }
        Console.WriteLine("Authorized drafts retain complete persisted/input scope and paired direct/group authority; denied writes preserve state and audit.");
    }

    private static async Task ExpectAsync(Func<Task> operation, FailureKind kind)
    {
        try { await operation(); }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == kind) { return; }
        throw new InvalidOperationException($"Expected draft refusal: {kind}.");
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("draft-save-probe");
    }

    private sealed class PauseAfterSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }
}
