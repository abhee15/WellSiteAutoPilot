using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>Reviewed input policy, shared by the production worker and trusted offline host.</summary>
public static class LogicModuleInputValidation
{
    public static void Validate(LogicInputValue input, LogicDataRequirement requirement, DateTimeOffset evaluationTimeUtc)
        => ValidateObservation(input.Quantity, input.Unit, input.TimestampUtc, input.Quality, requirement, evaluationTimeUtc);

    public static void ValidateObservation(string quantity, string? unit, DateTimeOffset timestampUtc,
        string quality, LogicDataRequirement requirement, DateTimeOffset evaluationTimeUtc)
    {
        if (requirement.Access != "Current") Fail("EXECUTION_INPUT_ACCESS_UNSUPPORTED");
        if (timestampUtc == default) Fail("EXECUTION_INPUT_TIMESTAMP_INVALID");
        if (!string.Equals(quantity, requirement.Quantity, StringComparison.OrdinalIgnoreCase) ||
            requirement.CanonicalUnit is not null && !string.Equals(unit, requirement.CanonicalUnit, StringComparison.OrdinalIgnoreCase))
            Fail("EXECUTION_INPUT_QUANTITY_MISMATCH");
        if (!string.Equals(quality, "Good", StringComparison.OrdinalIgnoreCase) &&
            !(requirement.AllowUncertainQuality && string.Equals(quality, "Uncertain", StringComparison.OrdinalIgnoreCase)))
            Fail("EXECUTION_INPUT_QUALITY_REJECTED");
        if (timestampUtc > evaluationTimeUtc.AddSeconds(30)) Fail("EXECUTION_INPUT_FUTURE");
        if (requirement.MaximumAgeSeconds is > 0 &&
            evaluationTimeUtc - timestampUtc > TimeSpan.FromSeconds(requirement.MaximumAgeSeconds.Value))
            Fail("EXECUTION_INPUT_STALE");
    }

    private static void Fail(string code) => throw new WellSiteAutoPilotException(code, FailureKind.Validation,
        "Execution input did not satisfy its declared requirements.");
}
