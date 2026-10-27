using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Assets;
using WellSiteAutoPilot.Persistence.ConfiguredLogic;
using WellSiteAutoPilot.Persistence.Connections;
using WellSiteAutoPilot.Persistence.Logic;
using WellSiteAutoPilot.Persistence.Executions;
using WellSiteAutoPilot.Persistence.Security;

internal static class AuthorizedExecutionReadChecks
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task RunAsync(DbContextOptions<WellSiteAutoPilotDbContext> options, Guid typeId)
    {
        await using var setup = new WellSiteAutoPilotDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var time = new Clock(now);
        var user = Guid.NewGuid();
        var role = Guid.NewGuid();
        var grant = Guid.NewGuid();
        var module = "read-plan-" + Guid.NewGuid().ToString("N");
        var hash = new string('d', 64);
        setup.Users.Add(new() { Id = user, IdentityKey = module, IdentityName = module, NormalizedIdentityName = module,
            IsActive = true, CreatedAtUtc = now, LastSeenAtUtc = now });
        setup.RoleDefinitions.Add(new() { Id = role, Key = module, Name = module, NormalizedName = module,
            IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
        setup.RolePermissions.AddRange(new RolePermissionEntity { RoleId = role, Permission = WellSitePermissions.ExecutionsRequest },
            new RolePermissionEntity { RoleId = role, Permission = WellSitePermissions.ConfiguredLogicRead });
        setup.AccessGrants.Add(new() { Id = grant, UserId = user, SubjectKind = "User", RoleId = role, ScopeKind = "ExactAssets",
            IsActive = true, Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
        setup.LogicModules.Add(new() { Id = Guid.NewGuid(), ModuleId = module, Version = "1.0.0", DisplayName = module,
            Publisher = "test", Runtime = "DotNet", ExecutionProfile = "test", ManifestJson = "{}", PackageSha256 = hash,
            TrustStatus = "CustomerApproved", IsEnabled = true, InstalledAtUtc = now });
        if (!await setup.LogicModulePackages.AnyAsync(p => p.Sha256 == hash))
            setup.LogicModulePackages.Add(new() { Sha256 = hash, Content = [1], UploadedAtUtc = now, UploadedByUserId = user });
        var subject = new AccessSubject(user);
        var actor = new SecurityActorContext(user, module, module);
        var cases = new List<(Guid Connection, Guid Asset, Guid Configuration, Guid Revision)>();
        foreach (var name in new[] { "a", "b" })
        {
            var connection = Guid.NewGuid(); var asset = Guid.NewGuid(); var configuration = Guid.NewGuid(); var revision = Guid.NewGuid();
            setup.Connections.Add(new() { Id = connection, Name = module + name, NormalizedName = module + name, AdapterKey = "foresite",
                SettingsVersion = 1, SettingsJson = "{\"baseUrl\":\"http://" + name + "/virtual/\"}", IsEnabled = true,
                Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now });
            setup.Assets.Add(new() { Id = asset, AssetTypeId = typeId, Name = module + name, OwnerConnectionId = connection,
                IsActive = true, CreatedAtUtc = now });
            setup.SourceBindings.Add(new() { Id = Guid.NewGuid(), ConnectionId = connection, AssetId = asset,
                ResourceKind = "foresite.well", ExternalId = "42", CreatedAtUtc = now });
            setup.AccessGrantAssets.Add(new() { GrantId = grant, AssetId = asset });
            setup.ConfiguredLogicDefinitions.Add(new() { Id = configuration, Name = module + name, ActiveRevisionId = revision, CreatedAtUtc = now });
            setup.ConfiguredLogicRevisions.Add(new() { Id = revision, ConfiguredLogicId = configuration, RevisionNumber = 1,
                ModuleId = module, ModuleVersion = "1.0.0", Mode = "Shadow", Status = "Active", CreatedAtUtc = now });
            cases.Add((connection, asset, configuration, revision));
        }
        await setup.SaveChangesAsync();
        var commands = cases.Select(c => new ConfiguredShadowExecutionCommand(c.Configuration, c.Revision, module, "1.0.0", "{}",
            [new("well", c.Asset, "{}")], [new("fillage", c.Asset, "foresite", "42", "PumpFillage", "Current", "%", 60, false, "{}")],
            ExecutionTriggerKind.Manual, null)).ToArray();
        var executions = new List<ExecutionRecord>();
        foreach (var command in commands)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            var execution = ExecutionService.CreateConfiguredRequest(command, module, now, Guid.NewGuid());
            executions.Add(await new ExecutionRepository(db, time).AddManualConfiguredRequestedAsync(execution, command, actor));
        }
        var plans = executions.Select(e => JsonSerializer.Deserialize<ExecutionRequestedV3>(e.RequestPayloadJson!, JsonOptions)!
            .Execution.Inputs.Single().Source!).ToArray();
        if (plans[0].ReadPlanId == plans[1].ReadPlanId || plans[0].ConnectionId == plans[1].ConnectionId)
            throw new InvalidOperationException("Identical external well IDs shared read authority.");
        for (var i = 0; i < cases.Count; i++)
        {
            var read = await ReadAsync(i); // Fresh context proves no in-memory plan registration is required.
            if (read.ConnectionId != cases[i].Connection || read.ExternalId != "42" || !read.Settings["baseUrl"]!.Contains(i == 0 ? "//a/" : "//b/", StringComparison.Ordinal))
                throw new InvalidOperationException("Read plan crossed saved connections.");
            if (executions[i].RequestPayloadJson!.Contains("baseUrl", StringComparison.Ordinal) ||
                executions[i].RequestPayloadJson!.Contains(user.ToString(), StringComparison.Ordinal))
                throw new InvalidOperationException("Worker snapshot contains settings or read principal.");
        }
        await DeniedAsync(() => ReadAsync(0, plans[1].ReadPlanId), "EXECUTION_READ_PLAN_INVALID");
        await DeniedAsync(() => ReadAsync(0, Guid.NewGuid()), "EXECUTION_READ_PLAN_INVALID");
        await setup.Connections.Where(c => c.Id == cases[0].Connection).ExecuteUpdateAsync(s => s.SetProperty(c => c.Revision, 2));
        await DeniedAsync(() => ReadAsync(0), "EXECUTION_READ_CONNECTION_CHANGED");
        await setup.Connections.Where(c => c.Id == cases[0].Connection).ExecuteUpdateAsync(s => s.SetProperty(c => c.Revision, 1).SetProperty(c => c.IsEnabled, false));
        await DeniedAsync(() => ReadAsync(0), "EXECUTION_CONNECTION_DISABLED");
        await setup.Connections.Where(c => c.Id == cases[0].Connection).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsEnabled, true));
        await setup.AccessGrants.Where(g => g.Id == grant).ExecuteUpdateAsync(s => s.SetProperty(g => g.IsActive, false));
        await DeniedAsync(() => ReadAsync(0), "SECURITY_SCOPE_DENIED");
        await setup.AccessGrants.Where(g => g.Id == grant).ExecuteUpdateAsync(s => s.SetProperty(g => g.IsActive, true));
        await setup.SourceBindings.Where(b => b.AssetId == cases[0].Asset).ExecuteUpdateAsync(s => s.SetProperty(b => b.ExternalId, "43"));
        await DeniedAsync(() => ReadAsync(0), "EXECUTION_READ_SOURCE_CHANGED");
        await setup.SourceBindings.Where(b => b.AssetId == cases[0].Asset).ExecuteUpdateAsync(s => s.SetProperty(b => b.ExternalId, "42"));
        await setup.ConfiguredLogicDefinitions.Where(c => c.Id == cases[0].Configuration).ExecuteUpdateAsync(s => s.SetProperty(c => c.ActiveRevisionId, (Guid?)null));
        await DeniedAsync(() => ReadAsync(0), "EXECUTION_READ_CONFIGURATION_CHANGED");
        await setup.ConfiguredLogicDefinitions.Where(c => c.Id == cases[0].Configuration).ExecuteUpdateAsync(s => s.SetProperty(c => c.ActiveRevisionId, cases[0].Revision));
        time.Now = now.AddMinutes(5);
        await DeniedAsync(() => ReadAsync(0), "EXECUTION_READ_PLAN_EXPIRED");
        time.Now = now;
        await setup.Executions.Where(e => e.Id == executions[0].Id).ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, "Completed"));
        await DeniedAsync(() => ReadAsync(0), "EXECUTION_READ_PLAN_INVALID");
        foreach (var input in new[] {
            commands[0].Inputs.Single() with { ProviderAssetExternalId = "43" },
            commands[0].Inputs.Single() with { Quantity = "unreviewed" },
            commands[0].Inputs.Single() with { ProviderMappingJson = "{\"path\":\"other\"}" },
            commands[0].Inputs.Single() with { ProviderId = "simulator" } })
        {
            var command = commands[0] with { Inputs = [input] };
            await DeniedAsync(async () =>
            {
                await using var db = new WellSiteAutoPilotDbContext(options);
                await new ExecutionRepository(db, time).AddManualConfiguredRequestedAsync(
                    ExecutionService.CreateConfiguredRequest(command, module, now, Guid.NewGuid()), command, actor);
            });
        }
        await setup.OutboxMessages.Where(m => m.Payload.Contains(module)).ExecuteDeleteAsync();
        await setup.Executions.Where(e => e.ModuleId == module).ExecuteDeleteAsync();
        Console.WriteLine("Authorized reads isolate connections, survive context restart, exclude settings and reject changed authority/source/configuration and expired/terminal requests.");

        async Task<AuthorizedExecutionRead> ReadAsync(int index, Guid? plan = null)
        {
            await using var db = new WellSiteAutoPilotDbContext(options);
            return await new ExecutionRepository(db, time).ResolveReadAsync(executions[index].Id, plan ?? plans[index].ReadPlanId);
        }
    }
    private static async Task DeniedAsync(Func<Task> action, string? code = null)
    {
        try { await action(); }
        catch (WellSiteAutoPilotException e) when (code is null || e.Code == code) { return; }
        throw new InvalidOperationException("Read authorization did not refuse " + code);
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
