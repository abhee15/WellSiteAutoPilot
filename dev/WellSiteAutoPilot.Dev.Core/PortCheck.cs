namespace WellSiteAutoPilot.Dev.Core;

public sealed record PortRequirement(string Service, int Port);

public static class PortCheck
{
    public static IReadOnlyList<string> FindConflicts(IEnumerable<PortRequirement> required, Func<int, bool> isInUse) =>
        required
            .Where(item => isInUse(item.Port))
            .Select(item =>
                $"Port {item.Port} ({item.Service}) is already in use. If the installed WellSite AutoPilot " +
                "services are running on this machine, stop them first.")
            .ToList();
}
