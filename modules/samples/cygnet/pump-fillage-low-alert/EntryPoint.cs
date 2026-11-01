using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.ModuleSdk;

namespace Weatherford.CygNet.PumpFillageLowAlert;

/// <summary>Raises a recommendation when current pump fillage is below a threshold. Deterministic; no provider client.</summary>
public sealed class EntryPoint : ILogicModuleV1
{
    public const string Low = "PUMP_FILLAGE_LOW";
    public const string Normal = "PUMP_FILLAGE_NORMAL";
    public const decimal DefaultThresholdPercent = 40m;

    public LogicModuleIdentity Identity { get; } = new("weatherford.cygnet.pump-fillage-low-alert", "1.0.0");

    public ValueTask<LogicModuleExecutionResult> ExecuteAsync(
        LogicModuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);
        if (context.ModuleIdentity != Identity) return Fail("MODULE_IDENTITY_MISMATCH");
        var asset = context.Assets.Count == 1 ? context.Assets.First() : null;
        if (asset is null || asset.Role != "well" || asset.AssetId == Guid.Empty) return Fail("ASSET_BINDING_INVALID");
        var input = context.Inputs.Count == 1 ? context.Inputs.First() : null;
        if (input is null || input.RequirementId != "pump-fillage" || input.AssetId != asset.AssetId)
            return Fail("INPUT_BINDING_INVALID");
        if (!string.Equals(input.Quantity, "PumpFillage", StringComparison.OrdinalIgnoreCase) || input.Unit != "%")
            return Fail("EXECUTION_INPUT_QUANTITY_MISMATCH");
        if (input.Value is < 0 or > 100) return Fail("INPUT_VALUE_INVALID");
        if (!TryThreshold(context.ParametersJson, out var threshold)) return Fail("PARAMETERS_INVALID");

        var observed = new EngineeringResultValue("pump-fillage", input.AssetId, input.Quantity, input.Value, input.Unit);
        if (input.Value >= threshold)
            return ValueTask.FromResult(new LogicModuleExecutionResult(Normal, [observed], [], [], new Dictionary<string, string>()));

        var detail = string.Create(CultureInfo.InvariantCulture,
            $"Pump fillage is {input.Value:0.#} %, below the {threshold:0.#} % threshold. Review the well for pump-off or a gas-locked pump.");
        return ValueTask.FromResult(new LogicModuleExecutionResult(Low, [observed],
            [new RecommendationIntentDraft(Low, input.AssetId, "Pump fillage is low", detail, input.Quantity, null, input.Unit)],
            [], new Dictionary<string, string>()));
    }

    private static bool TryThreshold(string? json, out decimal threshold)
    {
        threshold = DefaultThresholdPercent;
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty("lowFillagePercent", out var value)) return true;
            return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out threshold) && threshold is >= 0 and <= 100;
        }
        catch (JsonException) { return false; }
    }

    private static ValueTask<LogicModuleExecutionResult> Fail(string code) =>
        ValueTask.FromResult(new LogicModuleExecutionResult(code, [], [], [], new Dictionary<string, string>()) { FailureCode = code });
}
