using System.Globalization;
using System.Text;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Integration.Wami.Sample;

/// <summary>
/// Sample Logic Module: flags a well whose WAMI operating-point rate is below a target. Like every module it only sees
/// resolved inputs; the host reads ForeSite, fetches the model and calls WAMI (see <see cref="WellAnalysisScenario"/>).
/// Setting: {"minimumRate": number} in stb/d, default 250.
/// </summary>
public sealed class WellAnalysisModule : ILogicModuleV1
{
    public const string RateQuantity = "operatingPoint.liquidRate";

    public LogicModuleIdentity Identity { get; } = new("wami-sample.well-analysis", "0.1.0");

    public ValueTask<LogicModuleExecutionResult> ExecuteAsync(LogicModuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        var minimum = 250m;
        using (var settings = JsonDocument.Parse(string.IsNullOrWhiteSpace(context.ParametersJson) ? "{}" : context.ParametersJson))
        {
            if (settings.RootElement.TryGetProperty("minimumRate", out var value) && value.TryGetDecimal(out var parsed)) minimum = parsed;
        }

        var rate = context.Inputs.SingleOrDefault(input => input.Quantity == RateQuantity);
        if (rate is null || rate.Unit != "stb/d")
            return ValueTask.FromResult(new LogicModuleExecutionResult("INPUT_MISSING", [], [], [], new Dictionary<string, string>()));

        RecommendationIntentDraft[] recommendations = rate.Value < minimum
            ? [new("LOW_RATE", rate.AssetId, "Operating point below target",
                  string.Create(CultureInfo.InvariantCulture, $"WAMI operating point {rate.Value:0.#} {rate.Unit} is below {minimum:0.#}. Source {rate.Source}."))]
            : [];
        return ValueTask.FromResult(new LogicModuleExecutionResult("EVALUATED", [], recommendations, [], new Dictionary<string, string>()));
    }
}

/// <summary>A model "from ForeSite" read from a local file, for runs without a ForeSite server.</summary>
public sealed class FileModelArtifactSource(string path) : IModelArtifactSource
{
    public async ValueTask<ModelArtifact> GetCurrentModelAsync(string assetExternalId, CancellationToken cancellationToken = default) =>
        new(Path.GetFileName(path), "file", Path.GetExtension(path).TrimStart('.'), await File.ReadAllBytesAsync(path, cancellationToken));
}

/// <summary>A fixed real-time reading standing in for ForeSite current data.</summary>
public sealed class FixedWellheadPressure(decimal psia, TimeProvider clock) : ICurrentDataProvider
{
    public ValueTask<EngineeringValue> GetCurrentAsync(string assetExternalId, string quantity, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new EngineeringValue(quantity, psia, "psia", clock.GetUtcNow(), "Good", "sample-foresite"));
}

public static class WellAnalysisScenario
{
    public const string AssetExternalId = "597";

    /// <summary>The fake's own model format; only used when no model file is given.</summary>
    public static byte[] FakeModel() => Encoding.UTF8.GetBytes(
        "{\"productivityIndex\":2,\"reservoirPressure\":2500,\"tubingGradient\":0.35,\"depth\":6000}");

    public static async Task<(CompositionResult Composition, LogicModuleExecutionResult Module)> RunAsync(
        IWamiClient wami, IModelArtifactSource model, decimal wellheadPressurePsia, string settingsJson,
        TimeProvider? clock = null, CancellationToken cancellationToken = default)
    {
        clock ??= TimeProvider.System;
        var assetId = Guid.Parse("00000000-0000-4000-8000-000000000597");
        var registry = new ProviderRegistry()
            .AddCurrentData("sample-foresite", new FixedWellheadPressure(wellheadPressurePsia, clock))
            .AddModelSource("sample-foresite-models", model)
            .AddCalculation("sample-wami", wami);
        var plan = new CompositionPlan(
            "well-analysis",
            [new("data", DependencyCapability.CurrentData, "foresite"),
             new("model", DependencyCapability.ModelArtifact, "foresite"),
             new("calc", DependencyCapability.Calculation, "wami")],
            [new("data", "foresite", "sample-foresite", 1, assetId, AssetExternalId),
             new("model", "foresite", "sample-foresite-models", 1, assetId, AssetExternalId),
             new("calc", "wami", "sample-wami", 1, assetId, null)],
            "model", "calc", "nodal-analysis",
            [new("data", "WellheadPressure", "wellheadPressure", "psia")],
            TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10));

        var composition = await new MultiProviderCoordinator(registry, new AllowAll(), clock).RunAsync(plan, null, cancellationToken);
        var module = new WellAnalysisModule();
        var inputs = composition.Outputs
            .Where(output => output.Value is not null)
            .Select(output => new LogicInputValue(output.Quantity, assetId, output.Quantity, output.Value!.Value, output.Unit, output.TimestampUtc, output.Quality, output.Source))
            .ToArray();
        var result = await module.ExecuteAsync(new LogicModuleExecutionContext(
            Guid.NewGuid(), module.Identity, "wami-sample", clock.GetUtcNow(), settingsJson, [new LogicAssetContext("well", assetId, "{}")], inputs), cancellationToken);
        return (composition, result);
    }

    private sealed class AllowAll : IDependencyAuthorizer
    {
        public ValueTask<bool> IsAllowedAsync(DependencyBinding binding, DependencyCapability capability, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }

    public static void Print((CompositionResult Composition, LogicModuleExecutionResult Module) run)
    {
        Console.WriteLine($"composition: {run.Composition.Status} {run.Composition.FailureCode}");
        foreach (var step in run.Composition.Audit) Console.WriteLine($"  {step.Step,-10} {step.Role,-6} {step.Outcome,-10} {step.Detail}");
        foreach (var output in run.Composition.Outputs) Console.WriteLine($"  output {output.Quantity} = {output.Value} {output.Unit}");
        Console.WriteLine($"module: {run.Module.OutcomeCode}; recommendations: {string.Join(" | ", run.Module.Recommendations.Select(r => r.Code + ": " + r.Detail))}");
    }
}
