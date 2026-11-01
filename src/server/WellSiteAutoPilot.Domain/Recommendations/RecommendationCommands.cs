namespace WellSiteAutoPilot.Domain.Recommendations;

public static class RecommendationCommands
{
    /// <summary>
    /// The command recorded for an advisory recommendation: a proposal for a person to read, not an action on a device.
    /// It is reserved: no autonomy policy may permit it, and no module may emit it as a control command.
    /// </summary>
    public const string Advisory = "ADVISORY_REVIEW";
}
