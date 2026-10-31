using System.Globalization;
using System.Text.RegularExpressions;

namespace WellSiteAutoPilot.Application.Authoring;

public enum AuthoringIssueSeverity { Error = 1, Warning = 2, Info = 3 }

/// <summary>Path names elements by stable ID (for example steps/shortfall/operands/0), never by array position alone.</summary>
public sealed record AuthoringIssue(AuthoringIssueSeverity Severity, string Code, string Path, string Message);

public sealed record AuthoringValidationReport(
    bool IsValid, IReadOnlyList<AuthoringIssue> Issues, IReadOnlyList<string> StepOrder);

public static class AuthoringOperations
{
    /// <summary>Operation name to (minimum, maximum) operand count.</summary>
    public static readonly IReadOnlyDictionary<string, (int Min, int Max)> Arity =
        new Dictionary<string, (int, int)>(StringComparer.Ordinal)
        {
            ["add"] = (2, 8), ["subtract"] = (2, 2), ["multiply"] = (2, 8), ["divide"] = (2, 2),
            ["min"] = (2, 8), ["max"] = (2, 8), ["abs"] = (1, 1)
        };

    public static readonly IReadOnlyList<string> Comparators = ["lt", "le", "gt", "ge", "eq", "ne"];
}

public static partial class AuthoringDraftValidator
{
    public const string UnitDimensionless = "1";

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"^-?(0|[1-9][0-9]{0,27})(\.[0-9]{1,27})?$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalPattern();

    public static bool IsIdentifier(string? value) => value is not null && IdentifierPattern().IsMatch(value);

    public static bool IsCanonicalDecimal(string? value) =>
        value is not null && DecimalPattern().IsMatch(value) &&
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out _);

    private sealed record Resolved(bool Ok, string? Unit);

    /// <summary>Document-level static validation. It neither compiles nor executes anything.</summary>
    public static AuthoringValidationReport Validate(AuthoringDocument document)
    {
        var issues = new List<AuthoringIssue>();
        void Error(string code, string path, string message) =>
            issues.Add(new(AuthoringIssueSeverity.Error, code, path, message));
        void Warn(string code, string path, string message) =>
            issues.Add(new(AuthoringIssueSeverity.Warning, code, path, message));

        if (document.Schema != AuthoringDocument.SchemaId)
            Error("AUTHORING_SCHEMA_UNSUPPORTED", "schema", $"The document schema must be {AuthoringDocument.SchemaId}.");
        if (document.Profile != AuthoringDocument.DecimalProfile)
            Error("AUTHORING_PROFILE_UNSUPPORTED", "profile",
                $"Only the {AuthoringDocument.DecimalProfile} evaluation profile is supported.");

        var module = document.Module;
        if (!IsIdentifier(module.ModuleId))
            Error("AUTHORING_ID_INVALID", "module/moduleId", "The module ID must be 1-128 letters, digits, '.', '_' or '-'.");
        if (string.IsNullOrWhiteSpace(module.Version))
            Error("AUTHORING_FIELD_REQUIRED", "module/version", "A semantic version is required.");
        if (string.IsNullOrWhiteSpace(module.DisplayName))
            Error("AUTHORING_FIELD_REQUIRED", "module/displayName", "A display name is required.");
        if (string.IsNullOrWhiteSpace(module.Publisher))
            Error("AUTHORING_FIELD_REQUIRED", "module/publisher", "A publisher is required.");

        if (document.AssetRoles.Count != 1)
            Error("AUTHORING_ASSET_ROLE_UNSUPPORTED", "assetRoles", "This profile supports exactly one asset role.");
        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in document.AssetRoles)
        {
            if (!IsIdentifier(role.Role)) Error("AUTHORING_ID_INVALID", $"assetRoles/{role.Role}", "Asset role IDs are invalid.");
            else if (!roles.Add(role.Role)) Error("AUTHORING_ID_DUPLICATE", $"assetRoles/{role.Role}", "Asset role IDs must be unique.");
        }

        var inputs = Index(document.Inputs, i => i.Id, "inputs", Error);
        var parameters = Index(document.Parameters, p => p.Id, "parameters", Error);
        var steps = Index(document.Steps, s => s.Id, "steps", Error);
        var conditions = Index(document.Conditions, c => c.Id, "conditions", Error);
        Index(document.Outputs, o => o.Id, "outputs", Error);
        Index(document.Actions, a => a.Id, "actions", Error);

        if (document.Inputs.Count == 0) Error("AUTHORING_INPUT_REQUIRED", "inputs", "Declare at least one input.");
        foreach (var input in document.Inputs)
        {
            var path = $"inputs/{input.Id}";
            if (!roles.Contains(input.AssetRole)) Error("AUTHORING_REFERENCE_MISSING", path, "The input names an unknown asset role.");
            if (!IsIdentifier(input.Quantity)) Error("AUTHORING_ID_INVALID", path + "/quantity", "The quantity must be an identifier.");
            if (input.Unit is { Length: > 32 }) Error("AUTHORING_FIELD_INVALID", path + "/unit", "Units are at most 32 characters.");
            if (input.MaximumAgeSeconds is < 1) Error("AUTHORING_FIELD_INVALID", path + "/maximumAgeSeconds", "Maximum age must be positive.");
            if (input.ValueSchema is { } schema &&
                (string.IsNullOrWhiteSpace(schema.SchemaId) || schema.Schema.ValueKind != global::System.Text.Json.JsonValueKind.Object))
                Error("AUTHORING_SCHEMA_INVALID", path + "/valueSchema", "A value schema needs an identity and an object schema.");
        }
        var structured = document.Inputs.Count(i => i.ValueSchema is not null);
        if (structured != 0 && structured != document.Inputs.Count)
            Error("AUTHORING_INPUT_KIND_MIXED", "inputs", "Inputs must be all scalar or all structured (schema-identified).");

        foreach (var parameter in document.Parameters)
        {
            if (!IsCanonicalDecimal(parameter.DefaultValue))
                Error("AUTHORING_DECIMAL_INVALID", $"parameters/{parameter.Id}/defaultValue",
                    "Parameter defaults are canonical decimal text such as 2 or -0.5.");
        }

        var codeSteps = document.Steps.Where(s => s.Kind == "code").ToArray();
        var codeModule = codeSteps.Length > 0;
        var order = ValidateSteps(document, inputs, parameters, steps, codeModule, issues);

        foreach (var condition in document.Conditions)
        {
            var path = $"conditions/{condition.Id}";
            if (!AuthoringOperations.Comparators.Contains(condition.Comparator))
                Error("AUTHORING_COMPARATOR_INVALID", path + "/comparator", "The comparator must be lt, le, gt, ge, eq or ne.");
            var left = Resolve(condition.Left, path + "/left", document, inputs, parameters, steps, issues, requireScalar: true);
            var right = Resolve(condition.Right, path + "/right", document, inputs, parameters, steps, issues, requireScalar: true);
            if (left.Ok && right.Ok && left.Unit is not null && right.Unit is not null && left.Unit != right.Unit)
                Error("AUTHORING_UNIT_MISMATCH", path, $"Cannot compare {left.Unit} with {right.Unit}.");
            if (codeModule) Error("AUTHORING_OPERATION_UNSUPPORTED", path, "Conditions cannot be combined with a code-backed module step.");
        }

        if (document.Outputs.Count == 0) Error("AUTHORING_OUTPUT_REQUIRED", "outputs", "Declare at least one output.");
        foreach (var output in document.Outputs)
        {
            var path = $"outputs/{output.Id}";
            if (!IsIdentifier(output.Quantity)) Error("AUTHORING_ID_INVALID", path + "/quantity", "The quantity must be an identifier.");
            if (output.Unit.Length > 32) Error("AUTHORING_FIELD_INVALID", path + "/unit", "Units are at most 32 characters.");
            if (codeModule)
            {
                var step = steps.GetValueOrDefault(output.Source.Ref ?? "");
                if (output.Source.Kind != "step" || step?.Code is null || output.Source.Port is null ||
                    !step.Code.OutputPorts.Contains(output.Source.Port))
                    Error("AUTHORING_REFERENCE_MISSING", path + "/source", "A code-backed output must name the code step and one of its output ports.");
                if (output.ValueSchema is null)
                    Error("AUTHORING_SCHEMA_INVALID", path + "/valueSchema", "A code-backed output needs a value schema.");
                continue;
            }
            if (output.ValueSchema is not null)
                Error("AUTHORING_OPERATION_UNSUPPORTED", path + "/valueSchema", "Structured outputs need a code-backed step.");
            if (string.IsNullOrWhiteSpace(output.Unit)) Error("AUTHORING_FIELD_REQUIRED", path + "/unit", "An output unit is required.");
            var resolved = Resolve(output.Source, path + "/source", document, inputs, parameters, steps, issues, requireScalar: true);
            if (!resolved.Ok) continue;
            if (resolved.Unit is null)
                Warn("AUTHORING_UNIT_UNVERIFIED", path + "/unit", "The unit of this result cannot be derived; it is declared, not verified.");
            else if (resolved.Unit != output.Unit)
                Error("AUTHORING_UNIT_MISMATCH", path + "/unit", $"The source is in {resolved.Unit} but the output declares {output.Unit}.");
        }

        foreach (var action in document.Actions)
        {
            var path = $"actions/{action.Id}";
            if (!conditions.ContainsKey(action.Condition))
                Error("AUTHORING_REFERENCE_MISSING", path + "/condition", "The action names an unknown condition.");
            if (string.IsNullOrWhiteSpace(action.Title) || action.Title.Length > 200)
                Error("AUTHORING_FIELD_INVALID", path + "/title", "A title of up to 200 characters is required.");
            if (string.IsNullOrWhiteSpace(action.Detail) || action.Detail.Length > 1000)
                Error("AUTHORING_FIELD_INVALID", path + "/detail", "Detail of up to 1000 characters is required.");
            if (action.SuggestedValue is { } suggested)
            {
                var resolved = Resolve(suggested, path + "/suggestedValue", document, inputs, parameters, steps, issues, requireScalar: true);
                if (!IsIdentifier(action.Quantity) || string.IsNullOrWhiteSpace(action.Unit))
                    Error("AUTHORING_FIELD_REQUIRED", path, "A suggested value needs a quantity and a unit.");
                else if (resolved.Ok && resolved.Unit is not null && resolved.Unit != action.Unit)
                    Error("AUTHORING_UNIT_MISMATCH", path + "/unit", $"The suggested value is in {resolved.Unit}.");
            }
            if (action.Command is not null)
            {
                if (!IsIdentifier(action.Command))
                    Error("AUTHORING_FIELD_INVALID", path + "/command", "A command must be an identifier.");
                if (action.SuggestedValue is null)
                    Error("AUTHORING_FIELD_REQUIRED", path + "/suggestedValue", "A command proposal needs a suggested value.");
            }
            if (codeModule) Error("AUTHORING_OPERATION_UNSUPPORTED", path, "Actions cannot be combined with a code-backed module step.");
        }

        ValidateFixtures(document, inputs, parameters, issues);
        ReportUnused(document, issues);
        var valid = issues.All(i => i.Severity != AuthoringIssueSeverity.Error);
        return new AuthoringValidationReport(valid, issues, order);
    }

    private static Dictionary<string, T> Index<T>(IReadOnlyList<T> items, Func<T, string> id, string area,
        Action<string, string, string> error)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var key = id(item);
            if (!IsIdentifier(key)) error("AUTHORING_ID_INVALID", $"{area}/{key}", "IDs are 1-128 letters, digits, '.', '_' or '-'.");
            else if (!map.TryAdd(key, item)) error("AUTHORING_ID_DUPLICATE", $"{area}/{key}", "IDs must be unique.");
        }
        return map;
    }

    private static List<string> ValidateSteps(AuthoringDocument document, Dictionary<string, AuthoringInput> inputs,
        Dictionary<string, AuthoringParameter> parameters, Dictionary<string, AuthoringStep> steps, bool codeModule,
        List<AuthoringIssue> issues)
    {
        void Error(string code, string path, string message) =>
            issues.Add(new(AuthoringIssueSeverity.Error, code, path, message));
        if (codeModule)
        {
            if (document.Steps.Count != 1 || document.Parameters.Count != 0)
                Error("AUTHORING_CODE_STEP_EXCLUSIVE", "steps",
                    "A code-backed module step must be the only step and cannot be combined with parameters.");
            foreach (var step in document.Steps.Where(s => s.Kind == "code"))
                ValidateCodeStep(document, step, inputs, issues);
            foreach (var other in document.Steps.Where(s => s.Kind != "code"))
                Error("AUTHORING_OPERATION_UNSUPPORTED", $"steps/{other.Id}", "Calculation steps cannot be mixed with a code-backed module step.");
            return [];
        }

        foreach (var step in document.Steps)
        {
            var path = $"steps/{step.Id}";
            if (step.Kind != "calculation") { Error("AUTHORING_STEP_KIND_UNSUPPORTED", path, "Step kind must be calculation or code."); continue; }
            if (step.Operation is null || !AuthoringOperations.Arity.TryGetValue(step.Operation, out var arity))
            {
                Error("AUTHORING_OPERATION_UNSUPPORTED", path + "/operation",
                    "Supported operations: " + string.Join(", ", AuthoringOperations.Arity.Keys) + ".");
                continue;
            }
            var operands = step.Operands ?? [];
            if (operands.Count < arity.Min || operands.Count > arity.Max)
                Error("AUTHORING_ARITY_INVALID", path + "/operands", $"{step.Operation} takes {arity.Min}-{arity.Max} operands.");
            var units = new List<string?>();
            for (var index = 0; index < operands.Count; index++)
            {
                var resolved = Resolve(operands[index], $"{path}/operands/{index}", document, inputs, parameters, steps,
                    issues, requireScalar: true);
                units.Add(resolved.Unit);
            }
            if (step.Operation == "divide" && operands.Count == 2 && IsZero(operands[1], parameters, out var parameterDefault))
                issues.Add(new(parameterDefault ? AuthoringIssueSeverity.Warning : AuthoringIssueSeverity.Error,
                    "AUTHORING_DIVISION_BY_ZERO", path + "/operands/1", "The divisor is zero."));
            if (step.Operation is "add" or "subtract" or "min" or "max")
            {
                var known = units.Where(u => u is not null).Distinct().ToArray();
                if (known.Length > 1)
                    Error("AUTHORING_UNIT_MISMATCH", path, $"{step.Operation} mixes units {string.Join(", ", known)}.");
            }
        }

        var order = Order(document, steps);
        if (order is null)
        {
            Error("AUTHORING_CYCLE", "steps", "Calculation steps form a dependency cycle: " + string.Join(" -> ", FindCycle(document, steps)));
            return [];
        }
        return order;
    }

    private static void ValidateCodeStep(AuthoringDocument document, AuthoringStep step,
        Dictionary<string, AuthoringInput> inputs, List<AuthoringIssue> issues)
    {
        var path = $"steps/{step.Id}";
        void Error(string code, string detail, string message) =>
            issues.Add(new(AuthoringIssueSeverity.Error, code, path + detail, message));
        var code = step.Code;
        if (code is null) { Error("AUTHORING_FIELD_REQUIRED", "/code", "A code step carries its source."); return; }
        if (code.Language != "csharp") Error("AUTHORING_LANGUAGE_UNSUPPORTED", "/code/language", "Only csharp code steps are supported.");
        if (document.Inputs.Any(i => i.ValueSchema is null))
            Error("AUTHORING_INPUT_KIND_MIXED", "/code", "A code-backed module step consumes schema-identified (structured) inputs.");
        if (string.IsNullOrWhiteSpace(code.Source) || code.Source.Length > 256 * 1024)
            Error("AUTHORING_FIELD_INVALID", "/code/source", "Source is required and limited to 256 KiB.");
        else
        {
            if (!code.Source.Contains('"' + document.Module.ModuleId + '"', StringComparison.Ordinal) ||
                !code.Source.Contains('"' + document.Module.Version + '"', StringComparison.Ordinal))
                Error("AUTHORING_CODE_IDENTITY_MISMATCH", "/code/source",
                    "The source must declare the exact module ID and version of this draft.");
            var typeName = code.EntryPoint.Split('.')[^1];
            if (!Regex.IsMatch(code.Source, @"\bclass\s+" + Regex.Escape(typeName) + @"\b", RegexOptions.CultureInvariant))
                Error("AUTHORING_ENTRY_POINT_MISSING", "/code/entryPoint", "The source does not declare the entry point class.");
            issues.Add(new(AuthoringIssueSeverity.Info, "AUTHORING_CODE_STEP_OPAQUE", path,
                "Code step source is kept verbatim (SHA-256 " + AuthoringHashes.Sha256Text(code.Source) +
                "); its internal calculation graph is not interpreted."));
        }
        if (!AuthoringDraftValidator.IsTypeName(code.EntryPoint))
            Error("AUTHORING_FIELD_INVALID", "/code/entryPoint", "The entry point is a fully qualified type name.");
        foreach (var port in code.InputPorts)
            if (!inputs.ContainsKey(port)) Error("AUTHORING_REFERENCE_MISSING", "/code/inputPorts", $"Input port {port} is not a declared input.");
        if (code.OutputPorts.Count == 0 || code.OutputPorts.Any(p => !IsIdentifier(p)) || code.OutputPorts.Distinct().Count() != code.OutputPorts.Count)
            Error("AUTHORING_FIELD_INVALID", "/code/outputPorts", "Declare unique, valid output port IDs.");
    }

    public static bool IsTypeName(string? value) =>
        value is not null && Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant);

    private static bool IsZero(AuthoringOperand operand, Dictionary<string, AuthoringParameter> parameters, out bool fromParameter)
    {
        fromParameter = operand.Kind == "parameter";
        var text = operand.Kind == "literal" ? operand.Value
            : operand.Kind == "parameter" && operand.Ref is not null && parameters.TryGetValue(operand.Ref, out var p) ? p.DefaultValue : null;
        return IsCanonicalDecimal(text) && decimal.Parse(text!, CultureInfo.InvariantCulture) == 0m;
    }

    private static Resolved Resolve(AuthoringOperand operand, string path, AuthoringDocument document,
        Dictionary<string, AuthoringInput> inputs, Dictionary<string, AuthoringParameter> parameters,
        Dictionary<string, AuthoringStep> steps, List<AuthoringIssue> issues, bool requireScalar)
    {
        void Error(string code, string message) => issues.Add(new(AuthoringIssueSeverity.Error, code, path, message));
        switch (operand.Kind)
        {
            case "literal":
                if (!IsCanonicalDecimal(operand.Value)) { Error("AUTHORING_DECIMAL_INVALID", "Literals are canonical decimal text."); return new(false, null); }
                return new(true, null);
            case "input":
                if (operand.Ref is null || !inputs.TryGetValue(operand.Ref, out var input))
                { Error("AUTHORING_REFERENCE_MISSING", $"Unknown input {operand.Ref}."); return new(false, null); }
                if (requireScalar && input.ValueSchema is not null)
                { Error("AUTHORING_OPERATION_UNSUPPORTED", $"Input {input.Id} is structured; calculations need scalar inputs."); return new(false, null); }
                return new(true, string.IsNullOrEmpty(input.Unit) ? null : input.Unit);
            case "parameter":
                if (operand.Ref is null || !parameters.TryGetValue(operand.Ref, out var parameter))
                { Error("AUTHORING_REFERENCE_MISSING", $"Unknown parameter {operand.Ref}."); return new(false, null); }
                return new(true, string.IsNullOrEmpty(parameter.Unit) ? null : parameter.Unit);
            case "step":
                if (operand.Ref is null || !steps.TryGetValue(operand.Ref, out var step))
                { Error("AUTHORING_REFERENCE_MISSING", $"Unknown step {operand.Ref}."); return new(false, null); }
                if (step.Kind != "calculation")
                { Error("AUTHORING_OPERATION_UNSUPPORTED", "Only calculation steps can feed calculations."); return new(false, null); }
                return new(true, StepUnit(step, document, inputs, parameters, steps, new HashSet<string>()));
            default:
                Error("AUTHORING_REFERENCE_INVALID", "Operand kind must be input, parameter, step or literal.");
                return new(false, null);
        }
    }

    /// <summary>The derived unit of a step: null when unknown, "1" when dimensionless.</summary>
    private static string? StepUnit(AuthoringStep step, AuthoringDocument document, Dictionary<string, AuthoringInput> inputs,
        Dictionary<string, AuthoringParameter> parameters, Dictionary<string, AuthoringStep> steps, HashSet<string> visiting)
    {
        if (!visiting.Add(step.Id) || step.Operands is null) return null;
        string? UnitOf(AuthoringOperand operand) => operand.Kind switch
        {
            "input" when operand.Ref is not null && inputs.TryGetValue(operand.Ref, out var i) => string.IsNullOrEmpty(i.Unit) ? null : i.Unit,
            "parameter" when operand.Ref is not null && parameters.TryGetValue(operand.Ref, out var p) => string.IsNullOrEmpty(p.Unit) ? null : p.Unit,
            "step" when operand.Ref is not null && steps.TryGetValue(operand.Ref, out var s) && s.Kind == "calculation" =>
                StepUnit(s, document, inputs, parameters, steps, visiting),
            _ => null
        };
        var units = step.Operands.Select(UnitOf).ToArray();
        var unitless = step.Operands.Where(o => o.Kind == "literal").Count();
        var known = units.Where(u => u is not null).Distinct().ToArray();
        string? result = step.Operation switch
        {
            "add" or "subtract" or "min" or "max" or "abs" => known.Length == 1 ? known[0] : null,
            "multiply" => known.Length == 1 && units.Count(u => u is not null) == 1 && units.Count(u => u is null) == unitless
                ? known[0] : null,
            "divide" when units.Length == 2 && units[0] is not null && units[0] == units[1] => UnitDimensionless,
            "divide" when units.Length == 2 && units[0] is not null && step.Operands[1].Kind == "literal" => units[0],
            _ => null
        };
        visiting.Remove(step.Id);
        return result;
    }

    private static IEnumerable<string> StepDependencies(AuthoringStep step) =>
        (step.Operands ?? []).Where(o => o.Kind == "step" && o.Ref is not null).Select(o => o.Ref!);

    /// <summary>Topological order of calculation steps, or null when a cycle exists.</summary>
    public static List<string>? Order(AuthoringDocument document, Dictionary<string, AuthoringStep> steps)
    {
        var order = new List<string>();
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        bool Visit(string id)
        {
            if (!steps.TryGetValue(id, out var step)) return true;
            if (state.TryGetValue(id, out var current)) return current == 2;
            state[id] = 1;
            foreach (var dependency in StepDependencies(step))
                if (!Visit(dependency)) return false;
            state[id] = 2;
            order.Add(id);
            return true;
        }
        return document.Steps.Where(s => s.Kind == "calculation").All(s => Visit(s.Id)) ? order : null;
    }

    public static List<string> Order(AuthoringDocument document) =>
        Order(document, document.Steps.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)) ?? [];

    private static List<string> FindCycle(AuthoringDocument document, Dictionary<string, AuthoringStep> steps)
    {
        var path = new List<string>();
        var done = new HashSet<string>(StringComparer.Ordinal);
        List<string>? found = null;
        void Visit(string id)
        {
            if (found is not null || done.Contains(id) || !steps.TryGetValue(id, out var step)) return;
            var at = path.IndexOf(id);
            if (at >= 0) { found = [.. path.Skip(at), id]; return; }
            path.Add(id);
            foreach (var dependency in StepDependencies(step)) Visit(dependency);
            path.RemoveAt(path.Count - 1);
            done.Add(id);
        }
        foreach (var step in document.Steps) Visit(step.Id);
        return found ?? [];
    }

    private static void ValidateFixtures(AuthoringDocument document, Dictionary<string, AuthoringInput> inputs,
        Dictionary<string, AuthoringParameter> parameters, List<AuthoringIssue> issues)
    {
        void Error(string code, string path, string message) =>
            issues.Add(new(AuthoringIssueSeverity.Error, code, path, message));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var outputs = document.Outputs.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        var actions = document.Actions.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var fixture in document.Fixtures ?? [])
        {
            var path = $"fixtures/{fixture.Id}";
            if (!IsIdentifier(fixture.Id)) Error("AUTHORING_ID_INVALID", path, "Fixture IDs are 1-128 letters, digits, '.', '_' or '-'.");
            else if (!seen.Add(fixture.Id)) Error("AUTHORING_ID_DUPLICATE", path, "Fixture IDs must be unique.");
            foreach (var input in document.Inputs.Where(i => !fixture.Inputs.ContainsKey(i.Id)))
                Error("AUTHORING_FIXTURE_INPUT_MISSING", $"{path}/inputs/{input.Id}", "The fixture must supply a value for every input.");
            foreach (var (id, value) in fixture.Inputs)
            {
                if (!inputs.TryGetValue(id, out var input)) { Error("AUTHORING_REFERENCE_MISSING", $"{path}/inputs/{id}", "The fixture names an unknown input."); continue; }
                if (input.ValueSchema is null && !IsScalarFixtureValue(value))
                    Error("AUTHORING_DECIMAL_INVALID", $"{path}/inputs/{id}", "Scalar fixture values are JSON numbers or canonical decimal text.");
            }
            foreach (var (id, value) in fixture.Parameters ?? new Dictionary<string, string>())
            {
                if (!parameters.ContainsKey(id)) Error("AUTHORING_REFERENCE_MISSING", $"{path}/parameters/{id}", "The fixture names an unknown parameter.");
                else if (!IsCanonicalDecimal(value)) Error("AUTHORING_DECIMAL_INVALID", $"{path}/parameters/{id}", "Parameter values are canonical decimal text.");
            }
            foreach (var id in (fixture.ExpectedOutputs ?? new Dictionary<string, global::System.Text.Json.JsonElement>()).Keys.Where(k => !outputs.Contains(k)))
                Error("AUTHORING_REFERENCE_MISSING", $"{path}/expectedOutputs/{id}", "The fixture expects an unknown output.");
            foreach (var id in (fixture.ExpectedActions ?? []).Where(a => !actions.Contains(a)))
                Error("AUTHORING_REFERENCE_MISSING", $"{path}/expectedActions/{id}", "The fixture expects an unknown action.");
        }
    }

    private static bool IsScalarFixtureValue(global::System.Text.Json.JsonElement value) => value.ValueKind switch
    {
        global::System.Text.Json.JsonValueKind.Number => value.TryGetDecimal(out _),
        global::System.Text.Json.JsonValueKind.String => IsCanonicalDecimal(value.GetString()),
        _ => false
    };

    private static void ReportUnused(AuthoringDocument document, List<AuthoringIssue> issues)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        void Use(AuthoringOperand? operand) { if (operand?.Ref is { } r) used.Add(operand.Kind + ":" + r); }
        foreach (var step in document.Steps)
        {
            foreach (var operand in step.Operands ?? []) Use(operand);
            foreach (var port in step.Code?.InputPorts ?? []) used.Add("input:" + port);
        }
        foreach (var condition in document.Conditions) { Use(condition.Left); Use(condition.Right); }
        foreach (var output in document.Outputs) Use(output.Source);
        foreach (var action in document.Actions) Use(action.SuggestedValue);
        foreach (var input in document.Inputs.Where(i => !used.Contains("input:" + i.Id)))
            issues.Add(new(AuthoringIssueSeverity.Warning, "AUTHORING_INPUT_UNUSED", $"inputs/{input.Id}", "No calculation or output uses this input."));
        foreach (var parameter in document.Parameters.Where(p => !used.Contains("parameter:" + p.Id)))
            issues.Add(new(AuthoringIssueSeverity.Warning, "AUTHORING_PARAMETER_UNUSED", $"parameters/{parameter.Id}", "No calculation or condition uses this parameter."));
        foreach (var step in document.Steps.Where(s => s.Kind == "calculation" && !used.Contains("step:" + s.Id)))
            issues.Add(new(AuthoringIssueSeverity.Warning, "AUTHORING_STEP_UNUSED", $"steps/{step.Id}", "Nothing consumes this result."));
    }
}

public static class AuthoringHashes
{
    public static string Sha256Text(string text) =>
        Convert.ToHexStringLower(global::System.Security.Cryptography.SHA256.HashData(global::System.Text.Encoding.UTF8.GetBytes(text)));
}

