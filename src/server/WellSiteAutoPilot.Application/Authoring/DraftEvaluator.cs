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
    /// <summary>A required input has several possible sources and the author must choose a basis.</summary>
    NeedsSetup,
    /// <summary>A provider could not be reached or could not decide, so the inputs are unknown.</summary>
    Unavailable,
    /// <summary>The draft has errors and was not evaluated.</summary>
    Invalid
}

public sealed record CandidateInfo(string Source, string Basis, string Description);

/// <summary>What happened to one input: which source answered, or why none did.</summary>
public sealed record InputTrace(
    string Name, string Resolution, Val Value, string? Unit, string? Source, string? Basis, string? UsedInput,
    IReadOnlyList<string> Reasons, IReadOnlyList<CandidateInfo> Candidates, bool Required);

public sealed record NodeResult(string Name, Val Value, string? Expression, IReadOnlyList<string> DependsOn);

public sealed record OutputResult(string Name, string Kind, Val Value, string? Unit, string? Target, string Expression, SetPointWritePlan? Plan, string? Note = null);

public sealed record ModuleRun(
    ModuleOutcome Outcome, IReadOnlyList<string> Reasons, string Fingerprint,
    IReadOnlyList<InputTrace> Inputs, IReadOnlyList<NodeResult> Parameters, IReadOnlyList<NodeResult> Steps, IReadOnlyList<OutputResult> Outputs,
    IReadOnlyList<DraftProblem> Problems);

/// <summary>Evaluates a checked draft against already-resolved inputs. Pure: it reads nothing and writes nothing.</summary>
public static class DraftEvaluator
{
    private static int Rank(ModuleOutcome outcome) => outcome switch
    {
        ModuleOutcome.Runs => 0, ModuleOutcome.Unavailable => 1, ModuleOutcome.NeedsSetup => 2, _ => 3
    };

    public static ModuleRun Evaluate(ModuleDraft draft, IReadOnlyList<InputTrace> inputs, IReadOnlyDictionary<string, string>? parameterOverrides = null)
    {
        var analysis = DraftAnalyzer.Analyze(draft);
        if (!analysis.Valid)
            return new ModuleRun(ModuleOutcome.Invalid, ["Fix the problems in the module before testing it."], analysis.Fingerprint,
                inputs, [], [], [], analysis.Problems);

        var values = new Dictionary<string, Val>(StringComparer.Ordinal);
        foreach (var input in inputs) values[input.Name] = input.Value;
        var parameterResults = new List<NodeResult>();
        foreach (var parameter in draft.Parameters)
        {
            var text = parameterOverrides is not null && parameterOverrides.TryGetValue(parameter.Name, out var overridden) ? overridden : parameter.Value;
            var value = Val.Parse(text);
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
            var value = ModuleExpression.Parse(step.Expression).Evaluate(Lookup);
            values[name] = value;
            stepResults.Add(new NodeResult(name, value, step.Expression, graph[name].DependsOn));
        }

        var outputResults = draft.Outputs
            .Select(o => new OutputResult(o.Name, o.Kind, ModuleExpression.Parse(o.Expression).Evaluate(Lookup), o.Unit, o.Target, o.Expression, null))
            .ToList();

        var reasons = new List<string>();
        var outcome = ModuleOutcome.Runs;
        foreach (var input in inputs.Where(i => i.Required && !i.Value.Available))
        {
            reasons.Add($"{input.Name}: {string.Join(" ", input.Reasons.DefaultIfEmpty(input.Value.Reason ?? "no value"))}");
            var next = input.Resolution switch
            {
                "NeedsSelection" => ModuleOutcome.NeedsSetup,
                "CannotDetermine" => ModuleOutcome.Unavailable,
                _ => ModuleOutcome.Skipped
            };
            // A definite "does not apply" wins, then a choice the author must make, then an unreachable provider.
            if (Rank(next) > Rank(outcome)) outcome = next;
        }
        return new ModuleRun(outcome, reasons, analysis.Fingerprint, inputs, parameterResults, stepResults, outputResults, analysis.Problems);
    }
}
