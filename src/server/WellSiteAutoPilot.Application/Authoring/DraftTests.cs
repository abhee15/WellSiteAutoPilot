using System.Globalization;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;

namespace WellSiteAutoPilot.Application.Authoring;

public sealed record TestCheck(string Output, string Expected, string Actual, bool Passed);

public sealed record TestCaseResult(string Name, bool Passed, IReadOnlyList<TestCheck> Checks, string? Error = null);

/// <summary>The stored examples run, and whether the module maps onto a valid product manifest.</summary>
public sealed record VerifyResult(TestRunResult Tests, bool ManifestValid, string? ManifestError);

public sealed record TestRunResult(string Fingerprint, int Passed, int Failed, IReadOnlyList<TestCaseResult> Cases)
{
    public bool AllPassed => Failed == 0;
}

/// <summary>
/// Runs the examples stored with a module. An example is typed input values and the outputs they must produce; it needs
/// no connection, so it runs the same on any system, on import, and before a module is trusted.
/// </summary>
public static class DraftTests
{
    private const decimal DefaultTolerance = 0.0001m;

    public static TestRunResult Run(ModuleDraft draft, IReadOnlyList<TestCase>? cases = null, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.UtcNow;
        var results = new List<TestCaseResult>();
        var analysis = DraftAnalyzer.Analyze(draft);
        foreach (var test in cases ?? draft.Tests ?? [])
        {
            if (!analysis.Valid)
            {
                results.Add(new(test.Name, false, [], "The module has errors, so its examples cannot run."));
                continue;
            }
            var traces = draft.Inputs.Select(i => AuthoringService.FromSample(i, test.Inputs)).ToList();
            var run = DraftEvaluator.Evaluate(draft, traces, test.Parameters, at);
            var checks = test.Expect.Select(e => Check(e.Key, e.Value, run, test.Tolerance ?? DefaultTolerance)).ToList();
            results.Add(new(test.Name, checks.All(c => c.Passed), checks));
        }
        return new(analysis.Fingerprint, results.Count(r => r.Passed), results.Count(r => !r.Passed), results);
    }

    private static TestCheck Check(string name, string expected, ModuleRun run, decimal tolerance)
    {
        var output = run.Outputs.FirstOrDefault(o => o.Name == name);
        if (output is null)
            return new(name, expected, "no such output", false);

        var proposed = output.Kind != "setpoint" || output.Proposed == true;
        var actual = proposed ? output.Value : Val.Missing("not proposed");
        var shown = !actual.Available ? "unavailable" : actual.Display();
        var text = expected.Trim();

        if (string.Equals(text, "unavailable", StringComparison.OrdinalIgnoreCase))
            return new(name, "unavailable", shown, !actual.Available);
        if (!actual.Available)
            return new(name, text, shown, false);
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return new(name, text, shown, actual.Kind == ValueKind.Number && Math.Abs(actual.Number!.Value - number) <= tolerance);
        if (bool.TryParse(text, out var flag))
            return new(name, text, shown, actual.Kind == ValueKind.Flag && actual.Flag == flag);
        return new(name, text, shown, actual.Kind == ValueKind.Text && string.Equals(actual.Text, text, StringComparison.Ordinal));
    }
}

/// <summary>
/// Maps an authored module onto the product's existing logic module manifest, so authoring produces the same
/// contract the Worker and the configured-logic revisions already understand instead of a second format.
/// </summary>
public static class ManifestMapper
{
    public const string BuiltInEntryPoint = "wsa.declarative.v1";

    public static LogicModuleManifest ToManifest(ModuleDraft draft)
    {
        var moduleId = string.IsNullOrWhiteSpace(draft.ModuleId) ? Slug(draft.Name) : draft.ModuleId!;
        var roles = draft.Inputs.Select(i => i.Role ?? "well")
            .Concat(draft.Outputs.Where(o => o.Kind == "setpoint").Select(_ => "well"))
            .Distinct(StringComparer.Ordinal)
            .Select(r => new LogicAssetRequirement(r, 1, 1, null, []))
            .ToList();
        if (roles.Count == 0) roles.Add(new LogicAssetRequirement("well", 1, 1, null, []));

        var data = draft.Inputs.Select(i => new LogicDataRequirement(
            i.Name, i.Role ?? "well", i.Quantity, i.Basis == "History" ? "History" : "Current",
            Units.Normalize(i.Unit), i.MaxAgeSeconds ?? 3600, i.AllowUncertain)).ToList();
        var commands = draft.Outputs.Where(o => o.Kind == "setpoint")
            .Select(o => new LogicCommandRequirement(o.Name, "well", "SetPoint", o.Target, Units.Normalize(o.Unit))).ToList();

        return new LogicModuleManifest(
            1, moduleId, draft.Version, draft.Name, "Authored in WellSiteAutoPilot", LogicRuntimeKind.DotNet, ExecutionProfile.ScheduledOneShot,
            BuiltInEntryPoint, "1.0.0", roles, data, commands, draft.Description);
    }

    public static void Validate(ModuleDraft draft) => LogicModuleManifestValidator.Validate(ToManifest(draft));

    private static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "module" : slug;
    }
}
