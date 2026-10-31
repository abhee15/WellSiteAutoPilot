using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WellSiteAutoPilot.Application.Authoring;

/// <summary>Source files and manifest generated from a validated draft; the build host compiles and packs them.</summary>
public sealed record AuthoringGeneratedProject(
    string ManifestJson, IReadOnlyDictionary<string, string> SourceFiles, string AssemblyName)
{
    public const string ProjectAssemblyName = "Authored.Module";
}

public static class AuthoringModuleGenerator
{
    public const string SdkVersion = "1.2.0";
    public const string StructuredCapability = "structured.current";
    private const string GeneratedEntryPoint = "Authored.EntryPoint";

    public static AuthoringGeneratedProject Generate(AuthoringDocument document)
    {
        var code = document.Steps.FirstOrDefault(s => s.Kind == "code");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        if (code?.Code is { } step) files[$"Step_{code.Id}.cs"] = step.Source;
        else files["EntryPoint.cs"] = GenerateEntryPoint(document);
        return new(GenerateManifest(document, code), files, AuthoringGeneratedProject.ProjectAssemblyName);
    }

    public static string GenerateManifest(AuthoringDocument document, AuthoringStep? code = null)
    {
        code ??= document.Steps.FirstOrDefault(s => s.Kind == "code");
        var structured = code is not null;
        var module = document.Module;
        var manifest = new JsonObject
        {
            ["manifestVersion"] = structured ? 2 : 1,
            ["moduleId"] = module.ModuleId,
            ["version"] = module.Version,
            ["displayName"] = module.DisplayName,
            ["publisher"] = module.Publisher,
            ["runtime"] = "DotNet",
            ["executionProfile"] = "ScheduledOneShot",
            ["entryPoint"] = code?.Code?.EntryPoint ?? GeneratedEntryPoint,
            ["minimumSdkVersion"] = structured ? SdkVersion : "1.1.0"
        };
        if (module.RequiredAdapterKey is not null) manifest["requiredAdapterKey"] = module.RequiredAdapterKey;
        if (!string.IsNullOrWhiteSpace(module.Description)) manifest["description"] = module.Description;
        manifest["assetRequirements"] = new JsonArray(document.AssetRoles.Select(role => (JsonNode)new JsonObject
        {
            ["role"] = role.Role, ["minimumCount"] = 1, ["maximumCount"] = 1,
            ["requiredAssetTypeKey"] = role.RequiredAssetTypeKey, ["requiredTraits"] = new JsonArray()
        }).ToArray());
        manifest["dataRequirements"] = new JsonArray(document.Inputs.Select(input =>
        {
            var node = new JsonObject
            {
                ["id"] = input.Id, ["assetRole"] = input.AssetRole, ["quantity"] = input.Quantity, ["access"] = "Current",
                ["canonicalUnit"] = string.IsNullOrEmpty(input.Unit) ? null : input.Unit,
                ["maximumAgeSeconds"] = input.MaximumAgeSeconds, ["allowUncertainQuality"] = input.AllowUncertainQuality
            };
            if (input.ValueSchema is { } schema)
            {
                node["requiredCapability"] = input.RequiredCapability ?? StructuredCapability;
                node["valueSchema"] = SchemaNode(schema);
            }
            return (JsonNode)node;
        }).ToArray());
        manifest["commandRequirements"] = new JsonArray();
        if (structured)
            manifest["outputRequirements"] = new JsonArray(document.Outputs.Select(output => (JsonNode)new JsonObject
            {
                ["id"] = output.Id, ["quantity"] = output.Quantity,
                ["unit"] = string.IsNullOrEmpty(output.Unit) ? null : output.Unit,
                ["valueSchema"] = SchemaNode(output.ValueSchema!)
            }).ToArray());
        if (document.Parameters.Count > 0)
        {
            var properties = new JsonObject();
            foreach (var parameter in document.Parameters)
                properties[parameter.Id] = new JsonObject { ["type"] = "string", ["maxLength"] = 40, ["title"] = parameter.Label };
            manifest["parameterSchema"] = new JsonObject
            {
                ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false
            };
        }
        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject SchemaNode(AuthoringValueSchema schema) =>
        new() { ["schemaId"] = schema.SchemaId, ["schema"] = JsonNode.Parse(schema.Schema.GetRawText()) };

    public static string CSharpString(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (char.IsControl(c) || char.IsSurrogate(c) || c == '\u2028' || c == '\u2029')
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else builder.Append(c);
                    break;
            }
        }
        return builder.Append('"').ToString();
    }

    private static string Literal(string canonicalDecimal) =>
        canonicalDecimal.StartsWith('-') ? $"({canonicalDecimal}m)" : canonicalDecimal + "m";

    private static string GenerateEntryPoint(AuthoringDocument document)
    {
        var inputIndex = document.Inputs.Select((x, i) => (x.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var parameterIndex = document.Parameters.Select((x, i) => (x.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var stepOrder = AuthoringDraftValidator.Order(document);
        var stepIndex = stepOrder.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var conditionIndex = document.Conditions.Select((x, i) => (x.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var role = document.AssetRoles[0].Role;

        string Operand(AuthoringOperand o) => o.Kind switch
        {
            "input" => $"in{inputIndex[o.Ref!]}",
            "parameter" => $"par{parameterIndex[o.Ref!]}",
            "step" => $"st{stepIndex[o.Ref!]}",
            _ => Literal(o.Value!)
        };

        var code = new StringBuilder();
        void Line(string text) => code.Append("        ").AppendLine(text);
        code.AppendLine("// <auto-generated>Generated by WellSite AutoPilot authoring. Edit the draft, not this file.</auto-generated>");
        code.AppendLine("using System.Globalization;");
        code.AppendLine("using System.Text.Json;");
        code.AppendLine("using WellSiteAutoPilot.ModuleSdk;");
        code.AppendLine();
        code.AppendLine("namespace Authored;");
        code.AppendLine();
        code.AppendLine("public sealed class EntryPoint : ILogicModuleV1");
        code.AppendLine("{");
        code.AppendLine("    public LogicModuleIdentity Identity { get; } = new(" + CSharpString(document.Module.ModuleId) + ", " + CSharpString(document.Module.Version) + ");");
        code.AppendLine();
        code.AppendLine("    // Instrumentation contract: the test host reads the intermediate values of the last execution from here.");
        code.AppendLine("    private readonly Dictionary<string, string> _trace = new(StringComparer.Ordinal);");
        code.AppendLine("    public IReadOnlyDictionary<string, string> AuthoringTrace => _trace;");
        code.AppendLine();
        code.AppendLine("    public ValueTask<LogicModuleExecutionResult> ExecuteAsync(LogicModuleExecutionContext context, CancellationToken cancellationToken = default)");
        code.AppendLine("    {");
        code.AppendLine("        cancellationToken.ThrowIfCancellationRequested();");
        code.AppendLine("        try { return ValueTask.FromResult(Run(context)); }");
        code.AppendLine("        catch (OverflowException) { return ValueTask.FromResult(Failed(\"ARITHMETIC_OVERFLOW\")); }");
        code.AppendLine("    }");
        code.AppendLine();
        code.AppendLine("    private LogicModuleExecutionResult Run(LogicModuleExecutionContext context)");
        code.AppendLine("    {");
        Line("_trace.Clear();");
        Line("if (context is null || context.ModuleIdentity != Identity) return Failed(\"MODULE_IDENTITY_MISMATCH\");");
        Line("if (context.ExecutionId == Guid.Empty || string.IsNullOrWhiteSpace(context.CorrelationId) ||");
        Line("    context.EvaluationTimeUtc == default || context.EvaluationTimeUtc.Offset != TimeSpan.Zero) return Failed(\"CONTEXT_INVALID\");");
        Line($"if (context.Assets is null || context.Assets.Count != 1 || context.Assets.Single().Role != {CSharpString(role)} || context.Assets.Single().AssetId == Guid.Empty) return Failed(\"ASSET_BINDING_INVALID\");");
        Line("var assetId = context.Assets.Single().AssetId;");
        Line($"if (context.Inputs is null || context.Inputs.Count != {document.Inputs.Count}) return Failed(\"INPUT_BINDING_INVALID\");");
        Line("JsonElement settings;");
        Line("try { settings = JsonDocument.Parse(string.IsNullOrWhiteSpace(context.ParametersJson) ? \"{}\" : context.ParametersJson).RootElement; }");
        Line("catch (JsonException) { return Failed(\"CONTEXT_INVALID\"); }");
        Line("if (settings.ValueKind != JsonValueKind.Object) return Failed(\"CONTEXT_INVALID\");");
        foreach (var input in document.Inputs)
        {
            var n = inputIndex[input.Id];
            Line($"var input{n} = context.Inputs.SingleOrDefault(i => i.RequirementId == {CSharpString(input.Id)});");
            Line($"if (input{n} is null || input{n}.AssetId != assetId || !string.Equals(input{n}.Quantity, {CSharpString(input.Quantity)}, StringComparison.OrdinalIgnoreCase)) return Failed(\"INPUT_BINDING_INVALID\");");
            if (!string.IsNullOrEmpty(input.Unit))
                Line($"if (!string.Equals(input{n}.Unit, {CSharpString(input.Unit)}, StringComparison.OrdinalIgnoreCase)) return Failed(\"EXECUTION_INPUT_QUANTITY_MISMATCH\");");
            Line($"if (input{n}.TimestampUtc == default || input{n}.TimestampUtc.Offset != TimeSpan.Zero) return Failed(\"CONTEXT_INVALID\");");
            Line($"if (string.IsNullOrWhiteSpace(input{n}.Source)) return Failed(\"INPUT_SOURCE_MISSING\");");
            Line($"var in{n} = input{n}.Value;");
        }
        foreach (var parameter in document.Parameters)
        {
            var n = parameterIndex[parameter.Id];
            Line($"var par{n} = {Literal(parameter.DefaultValue)};");
            Line($"if (settings.TryGetProperty({CSharpString(parameter.Id)}, out var raw{n}))");
            Line($"{{ if (raw{n}.ValueKind != JsonValueKind.String || !decimal.TryParse(raw{n}.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out par{n})) return Failed(\"PARAMETER_INVALID\"); }}");
        }
        foreach (var id in stepOrder)
        {
            var step = document.Steps.First(s => s.Id == id);
            var operands = step.Operands!.Select(Operand).ToArray();
            var n = stepIndex[id];
            switch (step.Operation)
            {
                case "add": Line($"var st{n} = checked({string.Join(" + ", operands)});"); break;
                case "subtract": Line($"var st{n} = checked({operands[0]} - {operands[1]});"); break;
                case "multiply": Line($"var st{n} = checked({string.Join(" * ", operands)});"); break;
                case "divide":
                    Line($"if ({operands[1]} == 0m) return Failed(\"DIVISION_BY_ZERO\");");
                    Line($"var st{n} = checked({operands[0]} / {operands[1]});");
                    break;
                case "min": Line($"var st{n} = {operands.Aggregate((a, b) => $"Math.Min({a}, {b})")};"); break;
                case "max": Line($"var st{n} = {operands.Aggregate((a, b) => $"Math.Max({a}, {b})")};"); break;
                case "abs": Line($"var st{n} = Math.Abs({operands[0]});"); break;
            }
            Line($"_trace[{CSharpString(id)}] = st{n}.ToString(CultureInfo.InvariantCulture);");
        }
        foreach (var condition in document.Conditions)
        {
            var op = condition.Comparator switch { "lt" => "<", "le" => "<=", "gt" => ">", "ge" => ">=", "eq" => "==", _ => "!=" };
            Line($"var cond{conditionIndex[condition.Id]} = {Operand(condition.Left)} {op} {Operand(condition.Right)};");
            Line($"_trace[{CSharpString(condition.Id)}] = cond{conditionIndex[condition.Id]} ? \"true\" : \"false\";");
        }
        Line("var results = new List<EngineeringResultValue>();");
        foreach (var output in document.Outputs)
            Line($"results.Add(new EngineeringResultValue({CSharpString(output.Id)}, assetId, {CSharpString(output.Quantity)}, {Operand(output.Source)}, {CSharpString(output.Unit)}));");
        Line("var recommendations = new List<RecommendationIntentDraft>();");
        Line("var controlIntents = new List<ControlIntentDraft>();");
        foreach (var action in document.Actions)
        {
            var suggested = action.SuggestedValue is null ? "null" : Operand(action.SuggestedValue);
            var quantity = action.Quantity is null ? "null" : CSharpString(action.Quantity);
            var unit = action.Unit is null ? "null" : CSharpString(action.Unit);
            var intent = action.Id.ToUpperInvariant().Replace('-', '_').Replace('.', '_');
            if (action.Command is not null)
                Line($"if (cond{conditionIndex[action.Condition]}) controlIntents.Add(new ControlIntentDraft({CSharpString(intent)}, assetId, {CSharpString(action.Command)}, {quantity}, {suggested}, {unit}, {CSharpString(intent)}));");
            else
                Line($"if (cond{conditionIndex[action.Condition]}) recommendations.Add(new RecommendationIntentDraft({CSharpString(intent)}, assetId, {CSharpString(action.Title)}, {CSharpString(action.Detail)}, {quantity}, {suggested}, {unit}));");
        }
        Line("return new LogicModuleExecutionResult(\"AUTHORED_MODULE_COMPLETED\", results, recommendations, controlIntents, new Dictionary<string, string>());");
        code.AppendLine("    }");
        code.AppendLine();
        code.AppendLine("    private static LogicModuleExecutionResult Failed(string code) =>");
        code.AppendLine("        new(code, [], [], [], new Dictionary<string, string>()) { FailureCode = code };");
        code.AppendLine("}");
        return code.ToString();
    }
}

