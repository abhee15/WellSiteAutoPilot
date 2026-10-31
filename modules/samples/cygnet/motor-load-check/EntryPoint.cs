using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.ModuleSdk;

namespace Weatherford.CygNet.MotorLoadCheck;

/// <summary>Flags motor current or rod load above a limit. Deterministic; no provider client.</summary>
public sealed class EntryPoint : ILogicModuleV1
{
    public const string High = "MOTOR_LOAD_HIGH";
    public const string Normal = "MOTOR_LOAD_NORMAL";
    public const decimal DefaultMaxAmps = 60m;
    public const decimal DefaultMaxRodLoadLbf = 25000m;

    public LogicModuleIdentity Identity { get; } = new("weatherford.cygnet.motor-load-check", "1.0.0");

    public ValueTask<LogicModuleExecutionResult> ExecuteAsync(
        LogicModuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);
        if (context.ModuleIdentity != Identity) return Fail("MODULE_IDENTITY_MISMATCH");
        var asset = context.Assets.Count == 1 ? context.Assets.First() : null;
        if (asset is null || asset.Role != "well" || asset.AssetId == Guid.Empty) return Fail("ASSET_BINDING_INVALID");
        var amps = context.Inputs.SingleOrDefault(i => i.RequirementId == "motor-amps");
        var rod = context.Inputs.SingleOrDefault(i => i.RequirementId == "rod-load");
        if (context.Inputs.Count != 2 || amps is null || rod is null || amps.AssetId != asset.AssetId || rod.AssetId != asset.AssetId)
            return Fail("INPUT_BINDING_INVALID");
        if (amps.Quantity != "MotorAmps" || rod.Quantity != "RodLoad" || amps.Unit != "A" || rod.Unit != "lbf")
            return Fail("EXECUTION_INPUT_QUANTITY_MISMATCH");
        if (amps.Value < 0 || rod.Value < 0) return Fail("INPUT_VALUE_INVALID");
        if (!TryLimits(context.ParametersJson, out var maxAmps, out var maxRod)) return Fail("PARAMETERS_INVALID");

        EngineeringResultValue[] observed =
        [
            new("motor-amps", asset.AssetId, amps.Quantity, amps.Value, amps.Unit),
            new("rod-load", asset.AssetId, rod.Quantity, rod.Value, rod.Unit)
        ];
        var reasons = new List<string>();
        if (amps.Value > maxAmps) reasons.Add(string.Create(CultureInfo.InvariantCulture, $"motor current {amps.Value:0.#} A is above {maxAmps:0.#} A"));
        if (rod.Value > maxRod) reasons.Add(string.Create(CultureInfo.InvariantCulture, $"rod load {rod.Value:0.#} lbf is above {maxRod:0.#} lbf"));
        if (reasons.Count == 0)
            return ValueTask.FromResult(new LogicModuleExecutionResult(Normal, observed, [], [], new Dictionary<string, string>()));

        var detail = "The well is heavily loaded: " + string.Join(" and ", reasons) + ". Review pumping unit balance and downhole conditions.";
        return ValueTask.FromResult(new LogicModuleExecutionResult(High, observed,
            [new RecommendationIntentDraft(High, asset.AssetId, "Motor or rod load is high", detail)],
            [], new Dictionary<string, string>()));
    }

    private static bool TryLimits(string? json, out decimal maxAmps, out decimal maxRod)
    {
        maxAmps = DefaultMaxAmps;
        maxRod = DefaultMaxRodLoadLbf;
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var root = document.RootElement;
            if (root.TryGetProperty("maxMotorAmps", out var a) && !(a.ValueKind == JsonValueKind.Number && a.TryGetDecimal(out maxAmps) && maxAmps >= 0))
                return false;
            return !root.TryGetProperty("maxRodLoadLbf", out var r) ||
                r.ValueKind == JsonValueKind.Number && r.TryGetDecimal(out maxRod) && maxRod >= 0;
        }
        catch (JsonException) { return false; }
    }

    private static ValueTask<LogicModuleExecutionResult> Fail(string code) =>
        ValueTask.FromResult(new LogicModuleExecutionResult(code, [], [], [], new Dictionary<string, string>()) { FailureCode = code });
}
