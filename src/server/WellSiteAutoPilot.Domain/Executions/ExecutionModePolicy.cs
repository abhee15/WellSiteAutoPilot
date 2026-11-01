namespace WellSiteAutoPilot.Domain.Executions;

/// <summary>
/// What each operating mode may do. Shadow observes, Recommendation proposes to an operator, Autonomous acts through the
/// control gate (and only there). Autonomous is scheduler-driven: a person cannot start a run of an Autonomous revision.
/// Persisted-query filters are translated to SQL and cannot call this type, so they spell out the same sets; the
/// execution-mode safety tests fail if the two drift apart.
/// </summary>
public static class ExecutionModePolicy
{
    /// <summary>A revision or Execution may carry this mode.</summary>
    public static bool IsSupported(ExecutionMode mode) =>
        mode is ExecutionMode.Shadow or ExecutionMode.Recommendation or ExecutionMode.Autonomous;

    public static bool IsSupported(string? modeName) =>
        modeName is nameof(ExecutionMode.Shadow) or nameof(ExecutionMode.Recommendation) or nameof(ExecutionMode.Autonomous);

    /// <summary>A person may start a run of the active revision. Modes that act on their own are scheduler-only.</summary>
    public static bool AllowsManualRun(ExecutionMode mode) => mode is ExecutionMode.Shadow or ExecutionMode.Recommendation;

    /// <summary>The Execution's control intents become governed recommendations (and, for Autonomous, pass the gate).</summary>
    public static bool MaterializesControlIntents(ExecutionMode mode) =>
        mode is ExecutionMode.Recommendation or ExecutionMode.Autonomous;
}
