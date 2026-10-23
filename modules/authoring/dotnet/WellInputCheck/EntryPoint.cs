using System.Text.Json;
using WellSiteAutoPilot.ModuleSdk;

namespace Weatherford.ForeSiteWellInputCheck;

/// <summary>A deterministic calculation over supplied inputs; no provider client.</summary>
public sealed class EntryPoint : ILogicModuleV1
{
    public const string Success = "WELL_INPUT_CHECK_COMPLETED";
    public LogicModuleIdentity Identity { get; } =
        new("weatherford.foresite-well-input-check.dotnet", "1.0.0");

    public ValueTask<LogicModuleExecutionResult> ExecuteAsync(
        LogicModuleExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);
        var failure = Validate(context);
        if (failure is not null) return ValueTask.FromResult(Result(failure));
        var input = context.Inputs.Single();
        return ValueTask.FromResult(Result(Success,
            [new EngineeringResultValue("observed-value", input.AssetId, input.Quantity, input.Value, input.Unit)]));
    }

    private string? Validate(LogicModuleExecutionContext context)
    {
        if (context.ModuleIdentity != Identity) return "MODULE_IDENTITY_MISMATCH";
        if (context.ExecutionId == Guid.Empty || string.IsNullOrWhiteSpace(context.CorrelationId) ||
            context.EvaluationTimeUtc == default || context.EvaluationTimeUtc.Offset != TimeSpan.Zero ||
            !IsObject(context.ParametersJson)) return "CONTEXT_INVALID";
        if (context.Assets is null || context.Assets.Count != 1) return "ASSET_BINDING_INVALID";
        var asset = context.Assets.Single();
        if (asset is null || asset.Role != "well" || asset.AssetId == Guid.Empty) return "ASSET_BINDING_INVALID";
        if (!IsObject(asset.ParameterOverridesJson)) return "CONTEXT_INVALID";
        if (context.Inputs is null || context.Inputs.Count != 1) return "INPUT_BINDING_INVALID";
        var input = context.Inputs.Single();
        if (input is null || input.RequirementId != "pump-fillage" || input.AssetId != asset.AssetId)
            return "INPUT_BINDING_INVALID";
        if (!string.Equals(input.Quantity, "PumpFillage", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(input.Unit, "%", StringComparison.OrdinalIgnoreCase))
            return "EXECUTION_INPUT_QUANTITY_MISMATCH";
        if (!string.Equals(input.Quality, "Good", StringComparison.OrdinalIgnoreCase))
            return "EXECUTION_INPUT_QUALITY_REJECTED";
        if (input.TimestampUtc == default || input.TimestampUtc.Offset != TimeSpan.Zero) return "CONTEXT_INVALID";
        if (input.TimestampUtc - context.EvaluationTimeUtc > TimeSpan.FromSeconds(30)) return "EXECUTION_INPUT_FUTURE";
        if (context.EvaluationTimeUtc - input.TimestampUtc > TimeSpan.FromSeconds(60)) return "EXECUTION_INPUT_STALE";
        if (input.Value is < 0 or > 100) return "INPUT_VALUE_INVALID";
        if (string.IsNullOrWhiteSpace(input.Source)) return "INPUT_SOURCE_MISSING";
        return null;
    }

    private static bool IsObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; }
    }

    private static LogicModuleExecutionResult Result(string code, EngineeringResultValue[]? values = null) =>
        new(code, values ?? [], [], [], new Dictionary<string, string>());
}
