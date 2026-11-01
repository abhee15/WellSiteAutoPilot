namespace WellSiteAutoPilot.Application.Changes;

/// <summary>
/// The closed set of areas a live-update signal can name. Each area maps to
/// exactly one read permission at the host. See ADR-008.
/// </summary>
public enum ChangeArea
{
    Executions = 0,
    Recommendations = 1,
    ConfiguredLogic = 2,
    Assets = 3,
    Modules = 4,
    Connections = 5,
    Control = 6
}

/// <summary>
/// In-process change-notification seam. A producer names an area after its
/// transaction has committed. The signal carries no identifiers or values, so a
/// listener can never learn anything beyond "something in this area changed".
/// Implementations are best effort and must never throw or block the caller.
/// </summary>
public interface IChangeNotifier
{
    void Notify(ChangeArea area);
}

/// <summary>Default notifier for hosts and tests that do not push live updates.</summary>
public sealed class NoOpChangeNotifier : IChangeNotifier
{
    public static NoOpChangeNotifier Instance { get; } = new();

    private NoOpChangeNotifier()
    {
    }

    public void Notify(ChangeArea area)
    {
    }
}

public static class ChangeAreaNames
{
    public static IReadOnlyList<ChangeArea> All { get; } =
    [
        ChangeArea.Executions,
        ChangeArea.Recommendations,
        ChangeArea.ConfiguredLogic,
        ChangeArea.Assets,
        ChangeArea.Modules,
        ChangeArea.Connections,
        ChangeArea.Control
    ];

    /// <summary>The stable name sent to browsers (ADR-008).</summary>
    public static string ToWireName(ChangeArea area) => area switch
    {
        ChangeArea.Executions => "executions",
        ChangeArea.Recommendations => "recommendations",
        ChangeArea.ConfiguredLogic => "configured-logic",
        ChangeArea.Assets => "assets",
        ChangeArea.Modules => "modules",
        ChangeArea.Connections => "connections",
        ChangeArea.Control => "control",
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown change area.")
    };
}
