namespace WellSiteAutoPilot.Domain.Executions;

/// <summary>
/// Which operating modes the platform can govern today. A mode is accepted only when everything it implies is
/// implemented: Shadow observes, Recommendation proposes to an operator. Autonomous needs the control gate, which
/// is introduced separately and widens this policy with it; until then no Autonomous revision or execution can exist.
/// Persisted-query filters are translated to SQL and cannot call this type, so they spell out the same set; the
/// execution-mode safety tests fail if the two drift apart.
/// </summary>
public static class ExecutionModePolicy
{
    public static bool IsSupported(ExecutionMode mode) => mode is ExecutionMode.Shadow or ExecutionMode.Recommendation;

    public static bool IsSupported(string? modeName) =>
        modeName is nameof(ExecutionMode.Shadow) or nameof(ExecutionMode.Recommendation);

    /// <summary>A person may start a run of the active revision. Modes that act on their own are scheduler-only.</summary>
    public static bool AllowsManualRun(ExecutionMode mode) => IsSupported(mode);
}
