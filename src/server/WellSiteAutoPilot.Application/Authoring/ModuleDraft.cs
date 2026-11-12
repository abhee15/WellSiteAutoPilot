using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WellSiteAutoPilot.Application.Authoring;

/// <summary>Where an input reads from: a provider input name, optionally pinned to a basis (for example DailyAverage).</summary>
public sealed record InputSource(string Input, string? Basis = null);

/// <summary>
/// A named input. Sources are tried in order, so "casing pressure from surveillance, otherwise from the well test" is
/// two sources on one input. The provider and connection are bound at test and run time, not stored here.
/// </summary>
public sealed record DraftInput(string Name, IReadOnlyList<InputSource> Sources, bool Required = true, string? Label = null);

public sealed record DraftParameter(string Name, string Value, string? Label = null);

public sealed record DraftStep(string Name, string Expression, string? Label = null);

/// <summary>Kind "value" publishes a result. Kind "setpoint" proposes changing a controller set point by the calculated amount.</summary>
public sealed record DraftOutput(string Name, string Expression, string Kind = "value", string? Unit = null, string? Target = null, string? Label = null);

/// <summary>The portable authoring description of a logic module: inputs, parameters, named calculations and outputs.</summary>
public sealed record ModuleDraft(
    string Name, string? Description,
    IReadOnlyList<DraftInput> Inputs, IReadOnlyList<DraftParameter> Parameters,
    IReadOnlyList<DraftStep> Steps, IReadOnlyList<DraftOutput> Outputs);

public sealed record DraftProblem(string Severity, string Node, string Message);

/// <summary>One node of the dependency view: what it reads and what reads it.</summary>
public sealed record GraphNode(string Name, string Kind, IReadOnlyList<string> DependsOn, IReadOnlyList<string> UsedBy);

/// <summary>
/// The result of checking a draft: problems at the node that has them, the dependency graph for tracing, a
/// calculation order, and a fingerprint so test evidence can be tied to exactly this draft and go stale when it changes.
/// </summary>
public sealed record DraftAnalysis(bool Valid, IReadOnlyList<DraftProblem> Problems, IReadOnlyList<GraphNode> Graph, IReadOnlyList<string> Order, string Fingerprint);

public static partial class DraftAnalyzer
{
    public const int MaxNodes = 100;
    public const int MaxSources = 6;
    public static readonly IReadOnlyCollection<string> Bases = ["Static", "Current", "DailyAverage", "TestSample", "History"];
    public static readonly IReadOnlyCollection<string> OutputKinds = ["value", "setpoint"];

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,39}$")]
    private static partial Regex NamePattern();

    private static readonly JsonSerializerOptions FingerprintOptions = new(JsonSerializerDefaults.Web);

    public static string Fingerprint(ModuleDraft draft)
    {
        var json = JsonSerializer.Serialize(draft, FingerprintOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..12].ToLowerInvariant();
    }

    public static DraftAnalysis Analyze(ModuleDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var problems = new List<DraftProblem>();
        void Error(string? node, string message) => problems.Add(new("Error", node ?? "", message));
        void Warn(string? node, string message) => problems.Add(new("Warning", node ?? "", message));

        var inputs = draft.Inputs ?? [];
        var parameters = draft.Parameters ?? [];
        var steps = draft.Steps ?? [];
        var outputs = draft.Outputs ?? [];
        if (string.IsNullOrWhiteSpace(draft.Name))
            Error("module", "Give the module a name.");
        if (inputs.Count + parameters.Count + steps.Count + outputs.Count > MaxNodes)
            Error("module", $"A module can have at most {MaxNodes} inputs, parameters, calculations and outputs together.");
        if (outputs.Count == 0)
            Error("module", "Add at least one output, or the module has nothing to publish.");

        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        void Declare(string? name, string kind)
        {
            if (!NamePattern().IsMatch(name ?? ""))
                Error(name ?? "", "Names start with a letter and use only letters, digits and underscores (up to 40).");
            else if (name is "true" or "false" || ModuleExpression.Functions.Contains(name))
                Error(name, $"'{name}' is reserved.");
            else if (!kinds.TryAdd(name!, kind))
                Error(name, $"'{name}' is already used by a {kinds[name!]}.");
        }
        foreach (var input in inputs) Declare(input.Name, "input");
        foreach (var parameter in parameters) Declare(parameter.Name, "parameter");
        foreach (var step in steps) Declare(step.Name, "step");
        foreach (var output in outputs) Declare(output.Name, "output");

        foreach (var input in inputs)
        {
            var sources = input.Sources ?? [];
            if (sources.Count == 0) Error(input.Name, "Choose where this input comes from.");
            if (sources.Count > MaxSources) Error(input.Name, $"An input can have at most {MaxSources} sources.");
            foreach (var source in sources)
            {
                if (string.IsNullOrWhiteSpace(source.Input)) Error(input.Name, "A source needs a point name.");
                if (!string.IsNullOrEmpty(source.Basis) && !Bases.Contains(source.Basis, StringComparer.Ordinal))
                    Error(input.Name, $"Unknown basis '{source.Basis}'. Use one of {string.Join(", ", Bases)}.");
            }
        }
        foreach (var parameter in parameters)
            if (string.IsNullOrWhiteSpace(parameter.Value)) Error(parameter.Name, "A parameter needs a value.");

        var depends = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        void Read(string node, string expression)
        {
            try
            {
                var parsed = ModuleExpression.Parse(expression);
                var known = new List<string>();
                foreach (var reference in parsed.References)
                {
                    if (!kinds.TryGetValue(reference, out var kind)) Error(node, $"'{reference}' is not an input, parameter or calculation.");
                    else if (kind == "output") Error(node, $"'{reference}' is an output; outputs cannot feed other calculations.");
                    else if (reference == node) Error(node, "A calculation cannot use itself.");
                    else known.Add(reference);
                }
                depends[node] = known;
            }
            catch (ExpressionException error)
            {
                Error(node, error.Message);
                depends[node] = [];
            }
        }
        foreach (var step in steps) Read(step.Name ?? "", step.Expression);
        foreach (var output in outputs)
        {
            Read(output.Name ?? "", output.Expression);
            if (!OutputKinds.Contains(output.Kind, StringComparer.Ordinal)) Error(output.Name, $"Unknown output kind '{output.Kind}'.");
            if (output.Kind == "setpoint" && string.IsNullOrWhiteSpace(output.Target)) Error(output.Name, "Name the set point to change.");
        }

        var order = Order(steps, depends, problems);
        var usedBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (node, list) in depends)
            foreach (var dependency in list)
                (usedBy.TryGetValue(dependency, out var users) ? users : usedBy[dependency] = []).Add(node);
        foreach (var step in steps)
            if (kinds.ContainsKey(step.Name ?? "") && !usedBy.ContainsKey(step.Name!)) Warn(step.Name!, "Nothing uses this calculation.");
        foreach (var input in inputs)
            if (!usedBy.ContainsKey(input.Name ?? "")) Warn(input.Name ?? "", "Nothing uses this input.");
        foreach (var parameter in parameters)
            if (!usedBy.ContainsKey(parameter.Name ?? "")) Warn(parameter.Name ?? "", "Nothing uses this parameter.");

        IEnumerable<GraphNode> Graph()
        {
            foreach (var input in inputs) yield return Node(input.Name, "input");
            foreach (var parameter in parameters) yield return Node(parameter.Name, "parameter");
            foreach (var step in steps) yield return Node(step.Name, "step");
            foreach (var output in outputs) yield return Node(output.Name, "output");
        }
        GraphNode Node(string name, string kind) => new(name ?? "", kind,
            depends.TryGetValue(name ?? "", out var d) ? d : [],
            usedBy.TryGetValue(name ?? "", out var u) ? u.Order(StringComparer.Ordinal).ToList() : []);

        return new DraftAnalysis(problems.All(p => p.Severity != "Error"), problems, Graph().ToList(), order, Fingerprint(draft));
    }

    /// <summary>Steps in calculation order; a cycle is reported on every step involved.</summary>
    private static List<string> Order(IReadOnlyList<DraftStep> steps, Dictionary<string, IReadOnlyList<string>> depends, List<DraftProblem> problems)
    {
        var stepNames = steps.Select(s => s.Name ?? "").ToHashSet(StringComparer.Ordinal);
        var done = new List<string>();
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var cyclic = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string name, Stack<string> path)
        {
            if (state.TryGetValue(name, out var s))
            {
                if (s == 1)
                    foreach (var member in path.Reverse().SkipWhile(m => m != name).Append(name)) cyclic.Add(member);
                return;
            }
            state[name] = 1;
            path.Push(name);
            foreach (var dependency in depends.TryGetValue(name, out var list) ? list : [])
                if (stepNames.Contains(dependency)) Visit(dependency, path);
            path.Pop();
            state[name] = 2;
            done.Add(name);
        }
        foreach (var step in steps) Visit(step.Name ?? "", new Stack<string>());
        foreach (var name in cyclic.Order(StringComparer.Ordinal))
            problems.Add(new("Error", name, "This calculation depends on itself through other calculations."));
        return done;
    }
}
