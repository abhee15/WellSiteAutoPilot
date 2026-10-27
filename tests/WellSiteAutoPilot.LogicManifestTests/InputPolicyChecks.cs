using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;

internal static class InputPolicyChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.Parse("2026-10-27T12:00:00Z");
        var requirement = new LogicDataRequirement("pump-fillage", "well", "PumpFillage", "Current", "%", 60, false);
        var input = new LogicInputValue("pump-fillage", Guid.NewGuid(), "PumpFillage", 72.5m, "%", now.AddSeconds(-90), "Good", "synthetic");
        Reject(input, requirement, "EXECUTION_INPUT_STALE");
        Accept(input, requirement with { MaximumAgeSeconds = 120 });
        Accept(input with { TimestampUtc = now.AddSeconds(-60) }, requirement);
        Accept(input with { TimestampUtc = now.AddSeconds(30) }, requirement);
        Reject(input with { TimestampUtc = now.AddSeconds(31) }, requirement, "EXECUTION_INPUT_FUTURE");
        Accept(input with { TimestampUtc = now, Quality = "Uncertain" }, requirement with { AllowUncertainQuality = true });
        Reject(input with { TimestampUtc = now, Quality = "Uncertain" }, requirement, "EXECUTION_INPUT_QUALITY_REJECTED");
        foreach (var quality in new[] { "Bad", "Unknown", "" })
            Reject(input with { TimestampUtc = now, Quality = quality }, requirement with { AllowUncertainQuality = true }, "EXECUTION_INPUT_QUALITY_REJECTED");
        Reject(input with { TimestampUtc = default }, requirement, "EXECUTION_INPUT_TIMESTAMP_INVALID");
        Reject(input, requirement with { CanonicalUnit = "fraction" }, "EXECUTION_INPUT_QUANTITY_MISMATCH");
        Console.WriteLine("Shared manifest-derived input policy checks passed.");

        void Accept(LogicInputValue value, LogicDataRequirement policy) => LogicModuleInputValidation.Validate(value, policy, now);
        void Reject(LogicInputValue value, LogicDataRequirement policy, string code)
        {
            try { Accept(value, policy); }
            catch (WellSiteAutoPilotException failure) when (failure.Code == code) { return; }
            throw new InvalidOperationException("Input policy did not reject with " + code);
        }
    }
}
