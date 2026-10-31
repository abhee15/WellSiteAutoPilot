using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.ModuleSdk;

namespace Weatherford.CygNet.PressureDifferentialCheck;

/// <summary>Flags a casing pressure well above tubing pressure. Deterministic; no provider client.</summary>
public sealed class EntryPoint : ILogicModuleV1
{
    public const string High = "PRESSURE_DIFFERENTIAL_HIGH";
    public const string Normal = "PRESSURE_DIFFERENTIAL_NORMAL";
    public const decimal DefaultLimitPsi = 150m;

    public LogicModuleIdentity Identity { get; } = new("weatherford.cygnet.pressure-differential-check", "1.0.0");

    public ValueTask<LogicModuleExecutionResult> ExecuteAsync(
        LogicModuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);
        if (context.ModuleIdentity != Identity) return Fail("MODULE_IDENTITY_MISMATCH");
        var asset = context.Assets.Count == 1 ? context.Assets.First() : null;
        if (asset is null || asset.Role != "well" || asset.AssetId == Guid.Empty) return Fail("ASSET_BINDING_INVALID");
        var casing = context.Inputs.SingleOrDefault(i => i.RequirementId == "casing-pressure");
        var tubing = context.Inputs.SingleOrDefault(i => i.RequirementId == "tubing-pressure");
        if (context.Inputs.Count != 2 || casing is null || tubing is null || casing.AssetId != asset.AssetId || tubing.AssetId != asset.AssetId)
            return Fail("INPUT_BINDING_INVALID");
        if (casing.Quantity != "CasingPressure" || tubing.Quantity != "TubingPressure" || casing.Unit != "psi" || tubing.Unit != "psi")
            return Fail("EXECUTION_INPUT_QUANTITY_MISMATCH");
        if (casing.Value < 0 || tubing.Value < 0) return Fail("INPUT_VALUE_INVALID");
        if (!TryLimit(context.ParametersJson, out var limit)) return Fail("PARAMETERS_INVALID");

        var differential = casing.Value - tubing.Value;
        var result = new EngineeringResultValue("casing-tubing-differential", asset.AssetId, "CasingTubingDifferential", differential, "psi");
        if (differential <= limit)
            return ValueTask.FromResult(new LogicModuleExecutionResult(Normal, [result], [], [], new Dictionary<string, string>()));

        var detail = string.Create(CultureInfo.InvariantCulture,
            $"Casing pressure is {casing.Value:0.#} psi and tubing pressure {tubing.Value:0.#} psi, a difference of {differential:0.#} psi (limit {limit:0.#} psi). Check for gas accumulation or a blocked tubing string.");
        return ValueTask.FromResult(new LogicModuleExecutionResult(High, [result],
            [new RecommendationIntentDraft(High, asset.AssetId, "Casing pressure is high relative to tubing", detail,
                "CasingTubingDifferential", null, "psi")],
            [], new Dictionary<string, string>()));
    }

    private static bool TryLimit(string? json, out decimal limit)
    {
        limit = DefaultLimitPsi;
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty("maxDifferentialPsi", out var value)) return true;
            return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out limit) && limit is >= 0 and <= 10000;
        }
        catch (JsonException) { return false; }
    }

    private static ValueTask<LogicModuleExecutionResult> Fail(string code) =>
        ValueTask.FromResult(new LogicModuleExecutionResult(code, [], [], [], new Dictionary<string, string>()) { FailureCode = code });
}
