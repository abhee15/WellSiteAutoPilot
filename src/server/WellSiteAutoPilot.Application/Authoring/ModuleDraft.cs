using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WellSiteAutoPilot.Application.Authoring;

/// <summary>
/// A named input, described by what it means and not by where it lives: the quantity wanted (for example
/// CasingPressure), the unit the calculations expect, which basis it should have, and how old or doubtful a reading may
/// be before the module must not use it. Which provider point answers is a binding, not part of the module.
/// </summary>
public sealed record DraftInput(
    string Name, string Quantity, string? Unit = null, string? Basis = null, int? MaxAgeSeconds = null,
    bool AllowUncertain = false, bool Required = true, string? Role = null, string? Label = null);

public sealed record DraftParameter(string Name, string Value, string? Unit = null, string? Label = null);

public sealed record DraftStep(string Name, string Expression, string? Label = null);

/// <summary>Bounds a set point proposal must stay inside, whatever the calculation produced.</summary>
public sealed record OutputLimits(decimal? Min = null, decimal? Max = null, decimal? MaxStep = null);

/// <summary>
/// Kind "value" publishes a result. Kind "setpoint" proposes setting a controller set point to the calculated
/// value (an absolute value, never a change), but only when <c>When</c> is true and the proposal stays inside the limits.
/// </summary>
public sealed record DraftOutput(
    string Name, string Expression, string Kind = "value", string? Unit = null, string? Target = null,
    OutputLimits? Limits = null, string? When = null, string? Label = null);

/// <summary>A worked example stored with the module: typed input values and the outputs they must produce.</summary>
public sealed record TestCase(
    string Name, Dictionary<string, string> Inputs, Dictionary<string, string> Expect,
    Dictionary<string, string>? Parameters = null, decimal? Tolerance = null);

/// <summary>
/// The portable description of a logic module: what it needs, how it calculates, what it produces and examples that
/// prove it. It names no connection, provider or point, so it can be built on one system and imported on another.
/// </summary>
public sealed record ModuleDraft(
    string Name, string? Description,
    IReadOnlyList<DraftInput> Inputs, IReadOnlyList<DraftParameter> Parameters,
    IReadOnlyList<DraftStep> Steps, IReadOnlyList<DraftOutput> Outputs,
    IReadOnlyList<TestCase>? Tests = null, string? ModuleId = null, string Version = "0.1.0",
    int SchemaVersion = 1, int LanguageVersion = ModuleExpression.LanguageVersion);

/// <summary>Where one input is read from on this system: a provider input name, optionally pinned to a basis.</summary>
public sealed record BindingSource(string Input, string? Basis = null);

/// <summary>
/// The system-specific half of a module: which provider points answer each input (in order of preference) and which
/// controller set point each set point output changes. Nothing here travels with the module. An input with no
/// binding is read by its quantity and basis.
/// </summary>
public sealed record ModuleBindings(Dictionary<string, List<BindingSource>>? Inputs = null, Dictionary<string, string>? Targets = null);

public sealed record DraftProblem(string Severity, string Node, string Message);

/// <summary>One node of the dependency view: what it reads and what reads it.</summary>
public sealed record GraphNode(string Name, string Kind, IReadOnlyList<string> DependsOn, IReadOnlyList<string> UsedBy);

/// <summary>
/// The result of checking a draft: problems at the node that has them, the dependency graph for tracing, a
/// calculation order, and a content hash (SHA-256 of what the module means, ignoring labels, descriptions and examples)
/// so evidence can be tied to exactly this module.
/// </summary>
public sealed record DraftAnalysis(bool Valid, IReadOnlyList<DraftProblem> Problems, IReadOnlyList<GraphNode> Graph, IReadOnlyList<string> Order, string Fingerprint);

public static partial class DraftAnalyzer
{
    public const int MaxNodes = 100;
    public const int MaxTests = 50;
    public const int SchemaVersion = 1;
    public static readonly IReadOnlyCollection<string> Bases = ["Static", "Current", "DailyAverage", "TestSample", "History"];
    public static readonly IReadOnlyCollection<string> OutputKinds = ["value", "setpoint"];
    private static readonly string[] ProviderPrefixes = ["surveillance:", "welltest:", "well:", "setpoint:"];

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,39}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[a-z][a-z0-9]*([.\-][a-z0-9]+)*$")]
    private static partial Regex ModuleIdPattern();

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$")]
    private static partial Regex SemanticVersionPattern();

    private static readonly JsonSerializerOptions FingerprintOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Hash of what the module means. Labels, descriptions, examples, name and version are not meaning.</summary>
    public static string Fingerprint(ModuleDraft draft)
    {
        var canonical = new
        {
            draft.SchemaVersion, draft.LanguageVersion,
            Inputs = draft.Inputs?.Select(i => new { i.Name, Role = i.Role ?? "well", i.Quantity, Unit = Units.Normalize(i.Unit), i.Basis, i.MaxAgeSeconds, i.AllowUncertain, i.Required }),
            Parameters = draft.Parameters?.Select(p => new { p.Name, p.Value, Unit = Units.Normalize(p.Unit) }),
            Steps = draft.Steps?.Select(s => new { s.Name, Expression = s.Expression?.Trim() }),
            Outputs = draft.Outputs?.Select(o => new { o.Name, o.Kind, Expression = o.Expression?.Trim(), Unit = Units.Normalize(o.Unit), o.Target, o.Limits, When = o.When?.Trim() })
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical, FingerprintOptions))).ToLowerInvariant();
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
        var tests = draft.Tests ?? [];
        if (string.IsNullOrWhiteSpace(draft.Name))
            Error("module", "Give the module a name.");
        if (draft.SchemaVersion != SchemaVersion)
            Error("module", $"This module uses schema version {draft.SchemaVersion}; this system understands version {SchemaVersion}.");
        if (draft.LanguageVersion < 1 || draft.LanguageVersion > ModuleExpression.LanguageVersion)
            Error("module", $"This module was written for calculation language version {draft.LanguageVersion}; this system supports up to {ModuleExpression.LanguageVersion}.");
        if (draft.ModuleId is { Length: > 0 } id && (id.Length > 64 || !ModuleIdPattern().IsMatch(id)))
            Error("module", "The module id uses lower-case letters, digits, dots and hyphens (for example acme.pump-status).");
        if (!SemanticVersionPattern().IsMatch(draft.Version ?? ""))
            Error("module", "The version must look like 1.0.0.");
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

        void CheckUnit(string? node, string? unit)
        {
            if (!string.IsNullOrWhiteSpace(unit) && Units.DimensionOf(unit) is null)
                Warn(node, $"'{unit}' is not a unit this version can convert. Values in it are only compared with the same unit.");
        }
        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.Quantity))
                Error(input.Name, "Say what this input measures (for example CasingPressure).");
            else if (ProviderPrefixes.Any(p => input.Quantity.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                Warn(input.Name, "This names a provider point, so the module will not work on a system without that provider. Use the quantity, and bind the point on this system.");
            if (!string.IsNullOrEmpty(input.Basis) && !Bases.Contains(input.Basis, StringComparer.Ordinal))
                Error(input.Name, $"Unknown basis '{input.Basis}'. Use one of {string.Join(", ", Bases)}.");
            if (input.MaxAgeSeconds is <= 0)
                Error(input.Name, "The maximum age must be greater than zero seconds.");
            CheckUnit(input.Name, input.Unit);
        }
        foreach (var parameter in parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Value)) Error(parameter.Name, "A parameter needs a value.");
            CheckUnit(parameter.Name, parameter.Unit);
        }

        var depends = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        List<string> Reads(string node, string? expression, string what)
        {
            var known = new List<string>();
            try
            {
                foreach (var reference in ModuleExpression.Parse(expression).References)
                {
                    if (!kinds.TryGetValue(reference, out var kind)) Error(node, $"{what}: '{reference}' is not an input, parameter or calculation.");
                    else if (kind == "output") Error(node, $"{what}: '{reference}' is an output; outputs cannot feed other calculations.");
                    else if (reference == node) Error(node, "A calculation cannot use itself.");
                    else known.Add(reference);
                }
            }
            catch (ExpressionException error)
            {
                Error(node, $"{what}: {error.Message}");
            }
            return known;
        }
        foreach (var step in steps) depends[step.Name ?? ""] = Reads(step.Name ?? "", step.Expression, "Expression");
        foreach (var output in outputs)
        {
            var name = output.Name ?? "";
            var reads = Reads(name, output.Expression, "Expression");
            if (!OutputKinds.Contains(output.Kind, StringComparer.Ordinal)) Error(name, $"Unknown output kind '{output.Kind}'.");
            CheckUnit(name, output.Unit);
            if (output.Kind == "setpoint")
            {
                if (string.IsNullOrWhiteSpace(output.Target)) Error(name, "Name the set point to change.");
                if (output.Limits is null || output.Limits.Min is null && output.Limits.Max is null)
                    Warn(name, "No minimum or maximum is set. Only the controller's own range will stop an unreasonable proposal.");
                if (output.Limits is { MaxStep: <= 0 }) Error(name, "The maximum step must be greater than zero.");
                if (output.Limits is { Min: { } low, Max: { } high } && low > high) Error(name, "The minimum is above the maximum.");
                if (!string.IsNullOrWhiteSpace(output.When))
                    foreach (var extra in Reads(name, output.When, "Condition"))
                        if (!reads.Contains(extra, StringComparer.Ordinal)) reads.Add(extra);
            }
            else if (output.Limits is not null || !string.IsNullOrWhiteSpace(output.When))
                Warn(name, "Limits and conditions only apply to set point outputs.");
            depends[name] = reads;
        }

        var seenTests = new HashSet<string>(StringComparer.Ordinal);
        if (tests.Count > MaxTests) Error("module", $"A module can store at most {MaxTests} examples.");
        foreach (var test in tests)
        {
            var label = $"example '{test.Name}'";
            if (string.IsNullOrWhiteSpace(test.Name)) Error("module", "Every example needs a name.");
            else if (!seenTests.Add(test.Name)) Error("module", $"There are two examples named '{test.Name}'.");
            foreach (var name in (IEnumerable<string>?)test.Inputs?.Keys ?? [])
                if (!kinds.TryGetValue(name, out var kind) || kind != "input") Error("module", $"{label} gives a value for '{name}', which is not an input.");
            foreach (var name in (IEnumerable<string>?)test.Parameters?.Keys ?? [])
                if (!kinds.TryGetValue(name, out var kind) || kind != "parameter") Error("module", $"{label} overrides '{name}', which is not a parameter.");
            if ((test.Expect?.Count ?? 0) == 0) Error("module", $"{label} does not say what to expect.");
            foreach (var name in (IEnumerable<string>?)test.Expect?.Keys ?? [])
                if (!kinds.TryGetValue(name, out var kind) || kind != "output") Error("module", $"{label} expects '{name}', which is not an output.");
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
            usedBy.TryGetValue(name ?? "", out var u) ? u.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList() : []);

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
