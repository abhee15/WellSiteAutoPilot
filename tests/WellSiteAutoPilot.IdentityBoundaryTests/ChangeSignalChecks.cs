using System.Security.Claims;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WellSiteAutoPilot.Application.Changes;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Security;
using WellSiteAutoPilot.Server.Changes;
using WellSiteAutoPilot.Server.Security;

/// <summary>
/// ADR-008: the live-update signal carries only an area name, is sent only to users who
/// currently hold that area's read permission, and never trusts client-supplied claims.
/// </summary>
internal static class ChangeSignalChecks
{
    public static async Task RunAsync()
    {
        CheckAreaContract();
        await CheckAccessAsync();
        await CheckNotifierAsync();
        await CheckMonitorAsync();
        Console.WriteLine("Change signal names areas only, follows current read permissions, coalesces bursts and never fails producers.");
    }

    private static void CheckAreaContract()
    {
        string[] expected = ["executions", "recommendations", "configured-logic", "assets", "modules", "connections", "control"];
        Require(ChangeAreaNames.All.Select(ChangeAreaNames.ToWireName).SequenceEqual(expected),
            "Change area wire names drifted from ADR-008.");
        Require(Enum.GetValues<ChangeArea>().Length == ChangeAreaNames.All.Count,
            "A change area is missing from the closed list.");
        foreach (var area in ChangeAreaNames.All)
        {
            var permission = ChangeSignalProtocol.ReadPermission(area);
            Require(permission.EndsWith(".read", StringComparison.Ordinal) &&
                ProductPermissionCatalog.IsKnown(permission), $"{area} must map to a known read permission.");
        }
        Require(ChangeSignalProtocol.ReadPermission(ChangeArea.Modules) == WellSitePermissions.LogicRead,
            "Modules must follow logic.read.");
        Require(ChangeSignalProtocol.ReadPermission(ChangeArea.Connections) == WellSitePermissions.ConnectionsRead,
            "Connections must follow connections.read.");
        Require(ChangeSignalProtocol.ReadPermission(ChangeArea.Control) == WellSitePermissions.RecommendationsRead,
            "Control actions must follow recommendations.read, like the control routes.");
        Require(ChangeSignalProtocol.Route == "/hubs/changes" && !ChangeSignalProtocol.Route.StartsWith("/api/", StringComparison.Ordinal),
            "The hub must stay outside the reviewed API group.");
        ExpectThrows(() => ChangeAreaNames.ToWireName((ChangeArea)99), "unknown area");
        NoOpChangeNotifier.Instance.Notify(ChangeArea.Assets);

        var options = new ChangeSignalOptions();
        Require(options.RecheckInterval == TimeSpan.FromSeconds(60), "Default permission re-check must be 60 seconds.");
        Require(new ChangeSignalOptions { RecheckSeconds = 0 }.RecheckInterval == TimeSpan.FromSeconds(1) &&
            new ChangeSignalOptions { RecheckSeconds = 999999 }.RecheckInterval == TimeSpan.FromHours(1),
            "Re-check interval must be bounded.");
        Require(options.EffectiveTransports.HasFlag(HttpTransportType.LongPolling) &&
            new ChangeSignalOptions { Transports = HttpTransportType.None }.EffectiveTransports != HttpTransportType.None,
            "Long polling must stay available and an empty transport list must not disable the hub.");
    }

    private static async Task CheckAccessAsync()
    {
        var identities = new Identities();
        var grants = new Grants();
        var access = new ChangeAreaAccess(new UserIdentityService(identities, TimeProvider.System),
            new ConfiguredPrincipalResolver("TestHeader"), new NoGroups(), grants);

        // Claims claiming every permission, including a protected role, grant nothing.
        var claimed = Principal([.. WellSitePermissions.All.Select(item => new Claim("wsa:permission", item)),
            new Claim(ClaimTypes.Role, "Admin")]);
        Require((await access.GetReadableAreasAsync(claimed, CancellationToken.None)).Count == 0,
            "Client-supplied claims were trusted.");

        grants.Hints = [WellSitePermissions.ExecutionsRead, WellSitePermissions.AssetsRead,
            WellSitePermissions.AssetsManage, WellSitePermissions.ExecutionsRequest];
        var areas = await access.GetReadableAreasAsync(claimed, CancellationToken.None);
        ChangeArea[] executionsAndAssets = [ChangeArea.Executions, ChangeArea.Assets];
        Require(areas.SetEquals(executionsAndAssets),
            "Resource-scoped read hints did not map to their areas (manage/request must not).");

        grants.DeploymentAllowed = true;
        Require((await access.GetReadableAreasAsync(claimed, CancellationToken.None)).Contains(ChangeArea.Modules),
            "logic.read did not admit the modules area.");
        Require(!(await access.GetReadableAreasAsync(claimed, CancellationToken.None)).Contains(ChangeArea.Recommendations),
            "An area without its read permission was admitted.");

        grants.Hints = [];
        grants.DeploymentAllowed = false;
        Require((await access.GetReadableAreasAsync(claimed, CancellationToken.None)).Count == 0,
            "Revoked permissions were still honoured on the next check.");

        grants.Hints = WellSitePermissions.All;
        identities.Active = false;
        Require((await access.GetReadableAreasAsync(claimed, CancellationToken.None)).Count == 0,
            "A deactivated user was admitted.");
        identities.Active = true;
        var callsBefore = grants.Calls;
        Require((await access.GetReadableAreasAsync(new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None)).Count == 0 &&
            grants.Calls == callsBefore, "An anonymous principal reached the grant store.");
        grants.Throw = true;
        await ExpectFailureAsync(() => access.GetReadableAreasAsync(claimed, CancellationToken.None), "grant store failure");
    }

    private static async Task CheckNotifierAsync()
    {
        ChangeArea[] executionsAndAssets = [ChangeArea.Executions, ChangeArea.Assets];
        var sender = new Sender();
        var service = new ChangeSignalService(sender, Options.Create(new ChangeSignalOptions { CoalesceMilliseconds = 0 }),
            TimeProvider.System, NullLogger<ChangeSignalService>.Instance);
        for (var index = 0; index < 50; index++) service.Notify(ChangeArea.Executions);
        service.Notify(ChangeArea.Assets);
        service.Notify((ChangeArea)42);
        await service.FlushAsync(CancellationToken.None);
        Require(sender.Sent.SequenceEqual(executionsAndAssets),
            "A burst was not coalesced to one message per named area.");
        await service.FlushAsync(CancellationToken.None);
        Require(sender.Sent.Count == 2, "An already sent area was sent again.");

        // A failing transport must never surface to the producer or stop later areas.
        sender.Throw = true;
        service.Notify(ChangeArea.Modules);
        service.Notify(ChangeArea.Recommendations);
        await service.FlushAsync(CancellationToken.None);
        sender.Throw = false;
        service.Notify(ChangeArea.ConfiguredLogic);
        await service.FlushAsync(CancellationToken.None);
        Require(sender.Sent.Last() == ChangeArea.ConfiguredLogic, "The sender did not recover after a failure.");

        // The hosted loop delivers without an explicit flush.
        var hosted = new Sender();
        using var running = new ChangeSignalService(hosted, Options.Create(new ChangeSignalOptions { CoalesceMilliseconds = 10 }),
            TimeProvider.System, NullLogger<ChangeSignalService>.Instance);
        await running.StartAsync(CancellationToken.None);
        running.Notify(ChangeArea.Recommendations);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (hosted.Sent.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(20);
        await running.StopAsync(CancellationToken.None);
        ChangeArea[] recommendationsOnly = [ChangeArea.Recommendations];
        Require(hosted.Sent.SequenceEqual(recommendationsOnly), "The background loop did not send the area.");
    }

    private static async Task CheckMonitorAsync()
    {
        var registry = new ChangeConnectionRegistry();
        var fake = new Access { Areas = new HashSet<ChangeArea> { ChangeArea.Executions, ChangeArea.Assets, ChangeArea.Modules } };
        await using var provider = new ServiceCollection().AddSingleton<IChangeAreaAccess>(fake).BuildServiceProvider();
        var monitor = new ChangeConnectionMonitor(registry, provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ChangeSignalOptions()), TimeProvider.System, NullLogger<ChangeConnectionMonitor>.Instance);

        var principal = Principal([]);
        var kept = new Closeable();
        var lost = new Closeable();
        registry.Add(new ChangeConnection("kept", principal, new HashSet<ChangeArea> { ChangeArea.Executions, ChangeArea.Assets }, kept.Close));
        await monitor.RecheckAsync(CancellationToken.None);
        Require(kept.Closed == 0 && ReferenceEquals(fake.LastPrincipal, principal),
            "A connection that still holds its areas was closed, or the connection's own principal was not used.");

        registry.Add(new ChangeConnection("lost", principal, new HashSet<ChangeArea> { ChangeArea.Executions, ChangeArea.Recommendations }, lost.Close));
        await monitor.RecheckAsync(CancellationToken.None);
        Require(lost.Closed == 1 && kept.Closed == 0, "Losing one admitted area must close only that connection.");

        fake.Throw = true;
        await monitor.RecheckAsync(CancellationToken.None);
        Require(kept.Closed == 1, "A failed permission re-check must close the connection.");
        registry.Remove("kept");
        registry.Remove("lost");
        Require(registry.Count == 0, "Disconnected connections were not removed.");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        registry.Add(new ChangeConnection("late", principal, new HashSet<ChangeArea> { ChangeArea.Assets }, kept.Close));
        try { await monitor.RecheckAsync(cancelled.Token); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Shutdown cancellation was swallowed by the permission re-check.");
    }

    private static ClaimsPrincipal Principal(IEnumerable<Claim> extra) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "CI\\watcher"), .. extra], "HostAuthenticated",
            ClaimTypes.Name, ClaimTypes.Role));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectThrows(Action action, string label)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException(label + " was accepted.");
    }

    private static async Task ExpectFailureAsync(Func<Task> action, string label)
    {
        try { await action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException(label + " was treated as success.");
    }

    private sealed class Closeable
    {
        public int Closed { get; private set; }
        public void Close() => Closed++;
    }

    private sealed class Access : IChangeAreaAccess
    {
        public IReadOnlySet<ChangeArea> Areas { get; set; } = new HashSet<ChangeArea>();
        public bool Throw { get; set; }
        public ClaimsPrincipal? LastPrincipal { get; private set; }

        public Task<IReadOnlySet<ChangeArea>> GetReadableAreasAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
        {
            LastPrincipal = principal;
            if (Throw) throw new InvalidOperationException("Injected re-check failure.");
            return Task.FromResult(Areas);
        }
    }

    private sealed class Sender : IChangeSender
    {
        public List<ChangeArea> Sent { get; } = [];
        public bool Throw { get; set; }

        public Task SendAsync(ChangeArea area, CancellationToken cancellationToken)
        {
            if (Throw) throw new InvalidOperationException("Injected transport failure.");
            lock (Sent) Sent.Add(area);
            return Task.CompletedTask;
        }
    }

    private sealed class NoGroups : IIdentityGroupResolver
    {
        public VerifiedGroupMembership? Resolve(ClaimsPrincipal principal,
            AuthenticatedExternalIdentity identity, Guid localUserId) => null;
    }

    private sealed class Identities : IUserIdentityRepository
    {
        private readonly Guid userId = Guid.NewGuid();
        public bool Active { get; set; } = true;

        public Task<UserIdentity> UpsertIdentityAsync(string identityKey, string identityName, string normalizedIdentityName,
            string? displayName, DateTimeOffset seenAtUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UserIdentity(userId, identityKey, identityName, displayName, Active, seenAtUtc, seenAtUtc));

        public Task<UserIdentity?> GetIdentityAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The signal must not load user profiles.");

        public Task<IReadOnlyCollection<UserIdentity>> ListIdentitiesAsync(int limit, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The signal must not enumerate users.");
    }

    private sealed class Grants : IRequestAccessRepository
    {
        public IReadOnlyCollection<string> Hints { get; set; } = [];
        public bool DeploymentAllowed { get; set; }
        public bool Throw { get; set; }
        public int Calls { get; private set; }

        public Task<bool> HasAdministratorAsync(AccessSubject subject, CancellationToken cancellationToken = default) =>
            Observe(false);

        public Task<IReadOnlyCollection<string>> GetPermissionHintsAsync(AccessSubject subject, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("Injected grant failure.");
            return Task.FromResult(Hints);
        }

        public Task<bool> HasDeploymentPermissionAsync(AccessSubject subject, string permission, CancellationToken cancellationToken = default) =>
            Observe(DeploymentAllowed);

        public Task<bool> HasAllAssetPermissionAsync(AccessSubject subject, string permission, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The signal must not widen to AllAssets.");

        public Task<bool> HasAssetPermissionAsync(AccessSubject subject, string permission,
            IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The signal must not evaluate Asset scope.");

        private Task<bool> Observe(bool result)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("Injected grant failure.");
            return Task.FromResult(result);
        }
    }
}
