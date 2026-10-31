using System.Globalization;
using System.Reflection;
using System.Text.Json;
using WellSiteAutoPilot.Application.Authoring;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Worker.DotNet.Execution;

// Out-of-process test host for authoring. Usage: <package.wsamodule> <request.json> <result.json> <work-directory>
// It loads the built package with the production package host and runs each fixture case. It never reads providers.
if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: AuthoringTestHost <package> <request.json> <result.json> <work-directory>");
    return 2;
}

var json = JsonSerializerOptions.Web;
var results = new List<AuthoringTestHostCaseResult>();
string? error = null;
try
{
    var request = JsonSerializer.Deserialize<AuthoringTestHostRequest>(await File.ReadAllTextAsync(args[1]), json)!;
    var bytes = await File.ReadAllBytesAsync(args[0]);
    using (var stream = new MemoryStream(bytes))
    {
        var inspected = await LogicModulePackageReader.ReadAsync(stream, CancellationToken.None);
        await using var loaded = await new DotNetModulePackageHost(new FilePackageSource(bytes, request.Sha256), args[3])
            .LoadAsync(request.ModuleId, request.Version, request.Sha256, inspected.ManifestJson);
        StructuredModuleDefinition? definition = null;
        if (loaded.StructuredModule is not null)
        {
            await using var manifest = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(inspected.ManifestJson));
            definition = await StructuredModuleOfflineHost.ReadDefinitionAsync(manifest);
        }
        foreach (var testCase in request.Cases)
            results.Add(await RunCase(loaded, definition, request, testCase));
    }
}
catch (Exception failure)
{
    error = failure is WellSiteAutoPilotException known ? known.Code + ": " + known.SafeDetail : failure.GetType().Name + ": " + failure.Message;
}

await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(new AuthoringTestHostResult(results, error), json));
return 0;

static async Task<AuthoringTestHostCaseResult> RunCase(LoadedLogicModule loaded, StructuredModuleDefinition? definition,
    AuthoringTestHostRequest request, AuthoringTestHostCase testCase)
{
    var identity = new LogicModuleIdentity(request.ModuleId, request.Version);
    var evaluation = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    var observed = evaluation.AddSeconds(-1);
    var assets = new[] { new LogicAssetContext(testCase.AssetRole, request.AssetId, "{}") };
    object module = (object?)loaded.StructuredModule ?? loaded.Module;
    try
    {
        if (loaded.StructuredModule is { } structured)
        {
            var inputs = testCase.Inputs.Select(i => new StructuredInputValue(i.RequirementId, request.AssetId, i.Quantity,
                new StructuredValue(i.SchemaId!, i.Value.Clone()), i.Unit, observed, "Good", "authoring-test")).ToArray();
            var result = await StructuredModuleOfflineHost.ExecuteAsync(structured, definition!,
                new LogicModuleExecutionContextV2(Guid.NewGuid(), identity, "authoring-test", evaluation,
                    testCase.ParametersJson, assets, inputs));
            return Completed(testCase.Id, result.OutcomeCode,
                result.EngineeringResults.Select(r => new AuthoringTestHostOutput(r.Id, r.Quantity, r.Unit, r.Value.Data.Clone())),
                result.Recommendations, result.ControlIntents, Trace(module));
        }
        var scalars = testCase.Inputs.Select(i => new LogicInputValue(i.RequirementId, request.AssetId, i.Quantity,
            Decimal(i.Value), i.Unit ?? "", observed, "Good", "authoring-test")).ToArray();
        var v1 = await loaded.Module.ExecuteAsync(new LogicModuleExecutionContext(Guid.NewGuid(), identity,
            "authoring-test", evaluation, testCase.ParametersJson, assets, scalars));
        if (v1.FailureCode is { } code) return Failed(testCase.Id, code, null, Trace(module));
        return Completed(testCase.Id, v1.OutcomeCode,
            v1.EngineeringResults.Select(r => new AuthoringTestHostOutput(r.Id, r.Quantity, r.Unit,
                JsonSerializer.SerializeToElement(r.Value))),
            v1.Recommendations, v1.ControlIntents, Trace(module));
    }
    catch (WellSiteAutoPilotException failure)
    {
        return Failed(testCase.Id, failure.Code, failure.SafeDetail, Trace(module));
    }
    catch (Exception failure) when (failure is not OperationCanceledException)
    {
        return new(testCase.Id, "Error", null, null, failure.GetType().Name + ": " + failure.Message, [],
            Trace(module), []);
    }
}

static AuthoringTestHostCaseResult Completed(string id, string outcome, IEnumerable<AuthoringTestHostOutput> outputs,
    IEnumerable<RecommendationIntentDraft> recommendations, IEnumerable<ControlIntentDraft> controls,
    IReadOnlyDictionary<string, string> trace) =>
    new(id, "Completed", outcome, null, null, [.. outputs], trace,
    [
        .. recommendations.Select(r => new AuthoringTestHostAction("recommendation", r.Code, null, r.SuggestedValue)),
        .. controls.Select(c => new AuthoringTestHostAction("control", c.Code, c.Command, c.RequestedValue))
    ]);

static AuthoringTestHostCaseResult Failed(string id, string code, string? message, IReadOnlyDictionary<string, string> trace) =>
    new(id, "Failed", null, code, message, [], trace, []);

// Instrumentation contract: generated modules expose AuthoringTrace, the intermediate values of the last execution.
static IReadOnlyDictionary<string, string> Trace(object module) =>
    module.GetType().GetProperty("AuthoringTrace", BindingFlags.Public | BindingFlags.Instance)?.GetValue(module)
        is IReadOnlyDictionary<string, string> trace ? new Dictionary<string, string>(trace) : new Dictionary<string, string>();

static decimal Decimal(JsonElement value) => value.ValueKind == JsonValueKind.Number
    ? value.GetDecimal()
    : decimal.Parse(value.GetString()!, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

internal sealed class FilePackageSource(byte[] package, string sha256) : IModulePackageSource
{
    public async Task CopyToAsync(string requested, Stream destination, CancellationToken cancellationToken)
    {
        if (!string.Equals(requested, sha256, StringComparison.OrdinalIgnoreCase))
            throw new WellSiteAutoPilotException("MODULE_PACKAGE_NOT_AVAILABLE", FailureKind.DependencyUnavailable,
                "The requested package is not the package under test.");
        await destination.WriteAsync(package, cancellationToken);
    }
}
