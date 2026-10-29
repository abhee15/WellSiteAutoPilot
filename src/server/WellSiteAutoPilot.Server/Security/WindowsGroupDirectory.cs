using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Server.Security;

/// <summary>
/// Searches the server's joined AD domain using the server service identity.
/// No impersonation, supplied credentials, arbitrary LDAP addresses or membership fallback.
/// </summary>
public sealed class WindowsGroupDirectory : IGroupDirectory, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    public string DirectoryId => "windows-ad";
    public bool SupportsGroupSearch => OperatingSystem.IsWindows();

    public async Task<DirectoryGroup?> FindAsync(string externalId, CancellationToken cancellationToken)
    {
        if (!WindowsSidFormat.IsValid(externalId)) return null;
        return await RunAsync(() =>
        {
            if (!OperatingSystem.IsWindows()) throw Unavailable();
            using var context = new PrincipalContext(ContextType.Domain);
            using var group = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, externalId);
            return group?.IsSecurityGroup == true && group.Sid is not null && group.SamAccountName is { } name
                ? new DirectoryGroup(DirectoryId, group.Sid.Value, name, context.Name ?? "") : null;
        }, cancellationToken);
    }

    public async Task<GroupDirectoryPage> SearchAsync(string namePrefix, CancellationToken cancellationToken)
    {
        var prefix = GroupDirectorySearchService.ValidatePrefix(namePrefix);
        return await RunAsync(() =>
        {
            if (!OperatingSystem.IsWindows()) throw Unavailable();
            return Find(prefix);
        }, cancellationToken);
    }

    private async Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        if (!await gate.WaitAsync(0, cancellationToken)) throw Unavailable();
        var lookup = Task.Run(() =>
        {
            try { return operation(); }
            finally { gate.Release(); }
        }, CancellationToken.None);
        try { return await lookup.WaitAsync(Timeout, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { throw Unavailable(); }
    }

    [SupportedOSPlatform("windows")]
    private GroupDirectoryPage Find(string prefix)
    {
        using var context = new PrincipalContext(ContextType.Domain);
        using var filter = new GroupPrincipal(context) { IsSecurityGroup = true, SamAccountName = prefix + "*" };
        using var searcher = new PrincipalSearcher(filter);
        if (searcher.GetUnderlyingSearcher() is DirectorySearcher native)
        {
            native.SizeLimit = 51;
            native.ServerTimeLimit = Timeout;
            native.ClientTimeout = Timeout;
        }
        using var matches = searcher.FindAll();
        var groups = new List<DirectoryGroup>();
        foreach (var match in matches)
        {
            using (match)
            {
                if (match is not GroupPrincipal group || group.IsSecurityGroup != true || group.Sid is null) continue;
                var name = group.SamAccountName;
                if (string.IsNullOrWhiteSpace(name)) continue;
                groups.Add(new DirectoryGroup(DirectoryId, group.Sid.Value, name, context.Name ?? ""));
                if (groups.Count == 51) break;
            }
        }
        return new GroupDirectoryPage(groups.Take(50).OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.ExternalId, StringComparer.Ordinal).ToArray(), groups.Count > 50);
    }

    private static WellSiteAutoPilotException Unavailable() => new("SECURITY_DIRECTORY_UNAVAILABLE",
        FailureKind.DependencyUnavailable, "Group search is unavailable. Check domain connectivity and directory read access.");

    public void Dispose() => gate.Dispose();
}
