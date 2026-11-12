using System.Text.Json.Serialization;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Authoring;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModuleOutcome
{
    /// <summary>Every required input resolved. Individual outputs can still be unavailable, with a reason.</summary>
    Runs,
    /// <summary>A required input does not exist for this asset, so the module does not apply. Not a failure.</summary>
    Skipped,
    /// <summary>A required input needs a decision from the author (a basis, a unit, a binding) before it can run.</summary>
    NeedsSetup,
    /// <summary>A provider could not be reached, could not decide, or gave a reading too old or too doubtful to use.</summary>
    Unavailable,
    /// <summary>The draft has errors and was not evaluated.</summary>
    Invalid
}

public sealed record CandidateInfo(string Source, string Basis, string Description);

/// <summary>What happened to one input: which source answered, how fresh and trustworthy that answer was, or why none did.</summary>
public sealed record InputTrace(
    string Name, string Resolution, Val Value, string? Unit, string? Source, string? Basis, string? UsedInput,
    IReadOnlyList<string> Reasons, IReadOnlyList<CandidateInfo> Candidates, bool Required,
    long? AgeSeconds = null, int? MaxAgeSeconds = null, string? TimestampKind = null, string? Quantity = null);

public sealed record NodeResult(string Name, Val Value, string? Expression, IReadOnlyList<string> DependsOn, string? Matched = null);

/// <summary>One reason a set point proposal is or is not allowed. Status is Pass, Fail, Hold (condition not met) or Info.</summary>
public sealed record ProposalCheck(string Name, string Status, string Detail);

public sealed record OutputResult(
    string Name, string Kind, Val Value, string? Unit, string? Target, string Expression, SetPointWritePlan? Plan,
    string? Note = null, IReadOnlyList<ProposalCheck>? Checks = null, bool? Proposed = null, Advice? Advice = null);

/// <summary>A recommendation that fired: the outcome code, and the words shown to the person who reviews it.</summary>
public sealed record Advice(string Code, string Title, string Detail);

public sealed record ModuleRun(
    ModuleOutcome Outcome, IReadOnlyList<string> Reasons, string Fingerprint,
    IReadOnlyList<InputTrace> Inputs, IReadOnlyList<NodeResult> Parameters, IReadOnlyList<NodeResult> Steps, IReadOnlyList<OutputResult> Outputs,
    IReadOnlyList<DraftProblem> Problems, DateTimeOffset? EvaluatedAtUtc = null, string Mode = "sample", string? ExternalId = null);

/// <summary>
/// The rules for letting a provider reading into a module: usable quality, not too old, in the unit the module
/// expects. Shared by authoring tests and (later) production runs, so a reading is accepted or refused the same way in both.
/// </summary>
public static class InputAcceptance
{
    public sealed record Accepted(Val Value, string Resolution, string? Reason, long? AgeSeconds);

    public static DataQuality QualityOf(string? quality) => quality?.Trim().ToLowerInvariant() switch
    {
        "good" => DataQuality.Good,
        "uncertain" or "unknown" => DataQuality.Uncertain,
        _ => DataQuality.Bad
    };

    public static Accepted Accept(DraftInput input, decimal number, string? reportedUnit, DateTimeOffset observedAt, string? quality, DateTimeOffset now)
    {
        var age = (long)Math.Max(0, (now - observedAt).TotalSeconds);
        var grade = QualityOf(quality);
        if (grade == DataQuality.Bad || grade == DataQuality.Uncertain && !input.AllowUncertain)
            return Refuse("BadQuality", $"The reading's quality is {quality ?? "unknown"}, and this input does not accept that.", age);
        if (observedAt - now > TimeSpan.FromMinutes(5))
            return Refuse("Stale", "The reading is time-stamped in the future, so its age cannot be trusted.", age);
        if (input.MaxAgeSeconds is { } limit && age > limit)
            return Refuse("Stale", $"The reading is {Span(age)} old; this input allows {Span(limit)}.", age);

        var declared = Units.Normalize(input.Unit);
        var reported = Units.Normalize(reportedUnit);
        var value = number;
        var unit = reported ?? declared;
        // A reference-agnostic input (psi) accepts a gauge or absolute reading: the module has said the reference does not matter.
        var source = declared is not null && reported is not null && Units.DimensionOf(declared) == "pressure" && Units.WithoutReference(reported) is { } plain ? plain : reported;
        if (declared is not null && source is not null && !Units.TryConvert(number, source, declared, out value, out var why))
            return Refuse("UnitMismatch", $"The provider reports {reported} but this input expects {declared}: {why}", age);
        if (declared is not null) unit = declared;
        if (input.ValidMin is { } floor && value < floor || input.ValidMax is { } ceiling && value > ceiling)
            return Refuse("InvalidValue", $"The reading {Units.Format(value)} {unit} is outside the range this input accepts ({Units.Format(input.ValidMin, "no minimum")} to {Units.Format(input.ValidMax, "no maximum")}), so it is not trusted.", age);
        return new Accepted(Val.Of(value, unit) with { AsOf = observedAt, Quality = grade }, "Resolved", null, age);
    }

    private static Accepted Refuse(string resolution, string reason, long age) => new(Val.Missing(reason), resolution, reason, age);

    public static string Span(long seconds) =>
        seconds < 120 ? $"{seconds} s" : seconds < 7200 ? $"{seconds / 60} min" : seconds < 172800 ? $"{seconds / 3600} h" : $"{seconds / 86400} d";
}

/// <summary>Evaluates a checked draft against already-accepted inputs. Pure: it reads nothing and writes nothing.</summary>
public static class DraftEvaluator
{
    private static int Rank(ModuleOutcome outcome) => outcome switch
    {
        ModuleOutcome.Runs => 0, ModuleOutcome.Unavailable => 1, ModuleOutcome.NeedsSetup => 2, _ => 3
    };

    public static ModuleRun Evaluate(
        ModuleDraft draft, IReadOnlyList<InputTrace> inputs, IReadOnlyDictionary<string, string>? parameterOverrides = null, DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.UtcNow;
        var analysis = DraftAnalyzer.Analyze(draft);
        if (!analysis.Valid)
            return new ModuleRun(ModuleOutcome.Invalid, ["Fix the problems in the module before testing it."], analysis.Fingerprint,
                inputs, [], [], [], analysis.Problems, at);

        var values = new Dictionary<string, Val>(StringComparer.Ordinal);
        foreach (var input in inputs) values[input.Name] = input.Value;
        var parameterResults = new List<NodeResult>();
        foreach (var parameter in draft.Parameters)
        {
            var text = parameterOverrides is not null && parameterOverrides.TryGetValue(parameter.Name, out var overridden) ? overridden : parameter.Value;
            if (ParameterRange.Violation(parameter, text) is { } outOfRange)
                return new ModuleRun(ModuleOutcome.Invalid, [$"{parameter.Name} {outOfRange}"], analysis.Fingerprint, inputs, [], [], [],
                    [new DraftProblem("Error", parameter.Name, $"The value {outOfRange}")], at);
            var value = Val.Parse(text, parameter.Unit);
            values[parameter.Name] = value;
            parameterResults.Add(new NodeResult(parameter.Name, value, null, []));
        }

        var graph = analysis.Graph.ToDictionary(n => n.Name, StringComparer.Ordinal);
        Val Lookup(string name) => values.TryGetValue(name, out var v) ? v : Val.Missing($"'{name}' has no value");

        var stepResults = new List<NodeResult>();
        var stepsByName = draft.Steps.ToDictionary(s => s.Name, StringComparer.Ordinal);
        foreach (var name in analysis.Order)
        {
            var step = stepsByName[name];
            var (value, matched) = EvaluateStep(step, Lookup);
            values[name] = value;
            stepResults.Add(new NodeResult(name, value, step.Expression, graph[name].DependsOn, matched));
        }

        var outputResults = draft.Outputs.Select(o => EvaluateOutput(o, Lookup, at)).ToList();

        var reasons = new List<string>();
        var outcome = ModuleOutcome.Runs;
        foreach (var input in inputs.Where(i => i.Required && !i.Value.Available))
        {
            reasons.Add($"{input.Name}: {string.Join(" ", input.Reasons.DefaultIfEmpty(input.Value.Reason ?? "no value"))}");
            var next = input.Resolution switch
            {
                "NeedsSelection" or "Misconfigured" or "UnitMismatch" => ModuleOutcome.NeedsSetup,
                "CannotDetermine" or "Stale" or "BadQuality" or "InvalidValue" => ModuleOutcome.Unavailable,
                _ => ModuleOutcome.Skipped
            };
            // A definite "does not apply" wins, then a choice the author must make, then an unreachable or untrustworthy source.
            if (Rank(next) > Rank(outcome)) outcome = next;
        }
        // The module as a whole must run before any of its set points may be proposed.
        if (outcome != ModuleOutcome.Runs)
            outputResults = outputResults.Select(o => o.Kind == "setpoint"
                ? o with
                {
                    Proposed = false,
                    Checks = [.. o.Checks ?? [], new ProposalCheck("Module outcome", "Fail", $"The module did not run ({outcome}), so nothing is proposed.")],
                    Note = "Not proposed. Nothing was sent."
                }
                : o).ToList();
        return new ModuleRun(outcome, reasons, analysis.Fingerprint, inputs, parameterResults, stepResults, outputResults, analysis.Problems, at);
    }

    /// <summary>Plain steps evaluate their expression. Rule tables return the first matching row's value and say which row decided.</summary>
    private static (Val Value, string? Matched) EvaluateStep(DraftStep step, Func<string, Val> lookup)
    {
        if (step.Rules is not { Count: > 0 } rules)
            return (ModuleExpression.Parse(step.Expression).Evaluate(lookup), null);
        var row = 0;
        foreach (var rule in rules)
        {
            row++;
            var condition = ModuleExpression.Parse(rule.When).Evaluate(lookup);
            if (condition.Kind != ValueKind.Flag)
            {
                var why = condition.Available ? "must be true or false" : condition.Reason ?? "has no value";
                return (Val.Missing($"Rule {row} could not be decided: its condition {why}."), $"Rule {row} could not be decided");
            }
            if (condition.Flag == true)
            {
                var result = ModuleExpression.Parse(rule.Then).Evaluate(lookup).Derived(condition);
                return (result, $"Rule {row}" + (string.IsNullOrWhiteSpace(rule.Reason) ? "" : $": {rule.Reason}"));
            }
        }
        return (ModuleExpression.Parse(step.Expression).Evaluate(lookup), "No rule matched, so the otherwise value applies");
    }

    private static OutputResult EvaluateOutput(DraftOutput output, Func<string, Val> lookup, DateTimeOffset now)
    {
        var value = Finalize(ModuleExpression.Parse(output.Expression).Evaluate(lookup), output.Unit);
        if (output.Kind == "recommendation")
            return Recommend(output, value, lookup);
        if (output.Kind != "setpoint")
            return new OutputResult(output.Name, output.Kind, value, Units.Normalize(output.Unit), output.Target, output.Expression, null);

        var checks = new List<ProposalCheck>();
        var proceed = true;
        var evidence = value;
        if (value.Kind != ValueKind.Number)
        {
            checks.Add(new("Calculation", "Fail", value.Available ? "A set point needs a number." : value.Reason ?? "The calculation has no value."));
            proceed = false;
        }
        else
            checks.Add(new("Calculation", "Pass", $"Proposes {value.Display()}."));

        if (proceed && !string.IsNullOrWhiteSpace(output.When))
        {
            var condition = ModuleExpression.Parse(output.When).Evaluate(lookup);
            evidence = value.Derived(condition);
            if (!condition.Available)
            {
                checks.Add(new("Condition", "Fail", $"Cannot decide whether to act: {condition.Reason}"));
                proceed = false;
            }
            else if (condition.Kind != ValueKind.Flag)
            {
                checks.Add(new("Condition", "Fail", "The condition must be true or false."));
                proceed = false;
            }
            else if (condition.Flag == false)
            {
                checks.Add(new("Condition", "Hold", "The condition is not met, so nothing is proposed."));
                proceed = false;
            }
            else
                checks.Add(new("Condition", "Pass", "The condition is met."));
        }

        if (proceed)
        {
            var number = value.Number!.Value;
            if (output.Limits is { } limits && (limits.Min is not null || limits.Max is not null))
            {
                var inside = (limits.Min is null || number >= limits.Min) && (limits.Max is null || number <= limits.Max);
                checks.Add(new("Module limits", inside ? "Pass" : "Fail",
                    inside ? $"Inside {Bound(limits.Min)} to {Bound(limits.Max)}." : $"{Units.Format(number)} is outside the module's limits {Bound(limits.Min)} to {Bound(limits.Max)}."));
                proceed = inside;
            }
            if (evidence.Quality != DataQuality.Good)
            {
                checks.Add(new("Evidence quality", "Fail", $"The proposal rests on a reading of {evidence.Quality} quality."));
                proceed = false;
            }
            else if (evidence.AsOf is { } observed)
                checks.Add(new("Evidence age", "Info", $"The oldest reading behind this was observed {InputAcceptance.Span((long)Math.Max(0, (now - observed).TotalSeconds))} ago."));
        }
        checks.Add(new("Minimum interval", "Info", "Rate limits and cool-downs are enforced where the module runs, not in this test."));
        return new OutputResult(output.Name, output.Kind, value, Units.Normalize(output.Unit), output.Target, output.Expression, null,
            null, checks, proceed);
    }

    private static string Bound(decimal? value) => value is null ? "no limit" : Units.Format(value.Value);

    /// <summary>A recommendation fires only when its condition is definitely true; an undecidable condition never fires.</summary>
    private static OutputResult Recommend(DraftOutput output, Val condition, Func<string, Val> lookup)
    {
        var name = string.IsNullOrWhiteSpace(output.Code) ? DraftAnalyzer.DefaultCode(output.Name) : output.Code!;
        if (condition.Kind != ValueKind.Flag)
        {
            var why = condition.Available ? "The condition must be true or false." : condition.Reason ?? "The condition has no value.";
            return new OutputResult(output.Name, output.Kind, Val.Missing(why), null, null, output.Expression, null,
                "Cannot decide whether to recommend a review.", [new ProposalCheck("Condition", "Fail", why)], false);
        }
        if (condition.Flag != true)
            return new OutputResult(output.Name, output.Kind, condition, null, null, output.Expression, null,
                "Not recommended: the condition is not met.", [new ProposalCheck("Condition", "Hold", "The condition is not met, so nothing is recommended.")], false);
        var advice = new Advice(name, RecommendationText.Render(output.Title, lookup), RecommendationText.Render(output.Detail, lookup));
        return new OutputResult(output.Name, output.Kind, condition, null, null, output.Expression, null,
            "Recommends a review. Nothing is sent to a controller.", [new ProposalCheck("Condition", "Pass", "The condition is met.")], true, advice);
    }

    /// <summary>Holds an output to its declared unit: converts when the unit is compatible, refuses when it is not.</summary>
    private static Val Finalize(Val value, string? declaredUnit)
    {
        var declared = Units.Normalize(declaredUnit);
        if (value.Kind != ValueKind.Number || declared is null || value.Unit == declared)
            return value;
        if (value.Unit is null)
            return value with { Unit = declared };
        if (Units.DimensionOf(declared) == "pressure" && Units.WithoutReference(value.Unit) is { } plain)
            return Units.TryConvert(value.Number!.Value, plain, declared, out var relative, out _)
                ? value with { Number = relative, Unit = declared } : Val.Missing($"The result is in {value.Unit} but the output is declared in {declared}.");
        return Units.TryConvert(value.Number!.Value, value.Unit, declared, out var converted, out var why)
            ? value with { Number = converted, Unit = declared }
            : Val.Missing($"The result is in {value.Unit} but the output is declared in {declared}: {why}");
    }

    /// <summary>Completes a set point proposal once the controller has been asked: its range, the step size and the current value.</summary>
    public static OutputResult AfterPlan(OutputResult output, DraftOutput draft, SetPointWritePlan plan)
    {
        var checks = (output.Checks ?? []).ToList();
        var proposed = output.Proposed == true;
        checks.Add(new("Controller range", plan.Allowed ? "Pass" : "Fail",
            plan.Allowed ? $"Inside the controller's range {Bound(plan.RangeLow)} to {Bound(plan.RangeHigh)}." : plan.Refusal ?? "The controller refused the value."));
        if (!plan.Allowed) proposed = false;
        if (draft.Limits?.MaxStep is { } step)
        {
            var change = Math.Abs(plan.Proposed - plan.Current);
            var ok = change <= step;
            checks.Add(new("Maximum step", ok ? "Pass" : "Fail",
                ok ? $"A change of {Units.Format(change)} is within the {Units.Format(step)} step limit." : $"A change of {Units.Format(change)} is more than the {Units.Format(step)} step limit."));
            if (!ok) proposed = false;
        }
        checks.Add(new("Current value", "Info", "Taken from the controller's saved set point. Confirm it with a live readback before applying."));
        return output with { Plan = plan, Checks = checks, Proposed = proposed,
            Note = proposed ? "Dry run only. Nothing was sent." : "Not proposed. Nothing was sent." };
    }
}
