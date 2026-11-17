using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>
/// An authored, named sample case saved with a guided rule draft. <see cref="ExpectedOutcome"/> is the author's
/// assertion and is stored apart from anything the module produces: null means no assertion, <see cref="GuidedSampleCases.NoOutcome"/>
/// means the rule must not trigger, otherwise it must equal the rule's outcome code.
/// </summary>
public sealed record GuidedSampleCase(string Name, decimal? Value, decimal? CasingPsi, decimal? TubingPsi, string? ExpectedOutcome);

public static class GuidedSampleCases
{
    public const int Maximum = 50;
    public const int MaximumNameLength = 60;
    public const string NoOutcome = "NONE";

    /// <summary>Rejects cases that cannot describe the draft's calculation. Contract-only drafts have no rule, so no cases.</summary>
    public static void Validate(GuidedModuleDraftContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var cases = content.SampleCases;
        if (cases is null) return;
        if (content.Definition is not { } definition || cases.Count > Maximum)
            throw Invalid("Sample cases need a rule and at most " + Maximum + " entries.");
        var difference = content.Manifest.EntryPoint == DeclarativeThresholdModule.DifferenceEntryPoint;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in cases)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Name) || item.Name.Trim().Length > MaximumNameLength || !names.Add(item.Name.Trim()))
                throw Invalid("Each sample case needs a unique name of at most " + MaximumNameLength + " characters.");
            if (difference ? item.Value is not null : item.CasingPsi is not null || item.TubingPsi is not null)
                throw Invalid("Sample case '" + item.Name.Trim() + "' does not match the draft calculation.");
            if (item.ExpectedOutcome is not null && item.ExpectedOutcome != NoOutcome && item.ExpectedOutcome != definition.OutcomeCode)
                throw Invalid("Sample case '" + item.Name.Trim() + "' expects an outcome the rule cannot produce.");
        }
    }

    /// <summary>The outcome a run actually produced, or null when the case failed to evaluate.</summary>
    public static string? Observed(DeclarativeThresholdEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.FailureCode is not null || evidence.Result is null) return null;
        return evidence.Result.Recommendations.Count > 0 ? evidence.Result.OutcomeCode : NoOutcome;
    }

    /// <summary>Submission evidence is only trustworthy when every asserted case observed what the author expected.</summary>
    public static void RequireExpectations(IReadOnlyList<GuidedSampleCase> cases, IReadOnlyList<DeclarativeThresholdEvidence> evidence)
    {
        for (var index = 0; index < cases.Count; index++)
            if (cases[index].ExpectedOutcome is { } expected && Observed(evidence[index]) != expected)
                throw new WellSiteAutoPilotException("GUIDED_SAMPLE_EXPECTATION_FAILED", FailureKind.Validation,
                    "Sample case '" + cases[index].Name.Trim() + "' did not produce its expected outcome.");
    }

    private static WellSiteAutoPilotException Invalid(string message) =>
        new("GUIDED_SAMPLE_CASES_INVALID", FailureKind.Validation, message);
}
