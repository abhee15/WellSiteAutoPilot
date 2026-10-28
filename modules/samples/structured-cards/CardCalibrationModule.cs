using System.Text.Json;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Samples.StructuredCards;

// Synthetic calibration example, NOT Gibbs/Everitt-Jennings downhole analysis.
// It proves that a module can consume card collections plus a configuration record
// and return both curves and a scalar. No provider or customer UDC knowledge here.
public sealed class CardCalibrationModule : ILogicModuleV2
{
    public LogicModuleIdentity Identity { get; } = new("sample.card-calibration", "1.0.0");

    public ValueTask<LogicModuleExecutionResultV2> ExecuteAsync(LogicModuleExecutionContextV2 context,
        CancellationToken cancellationToken = default)
    {
        var cards = context.Inputs.Single(i => i.RequirementId == "cards");
        var configuration = context.Inputs.Single(i => i.RequirementId == "calibration").Value.Data;
        var scale = configuration.GetProperty("loadScale").GetDecimal();
        var offset = configuration.GetProperty("loadOffset").GetDecimal();
        var calibrated = new List<object>();
        decimal? peakLoad = null;
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in cards.Value.Data.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var position = card.GetProperty("position").EnumerateArray().Select(p => p.GetDecimal()).ToArray();
            var load = card.GetProperty("load").EnumerateArray().Select(p => p.GetDecimal()).ToArray();
            var captureText = card.GetProperty("capturedAtUtc").GetString()!;
            if (position.Length != load.Length || !identities.Add(card.GetProperty("id").GetString()!) ||
                !captureText.EndsWith('Z') ||
                !DateTimeOffset.TryParse(captureText, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var capturedAt) ||
                capturedAt > context.EvaluationTimeUtc.AddSeconds(30))
                return ValueTask.FromResult(new LogicModuleExecutionResultV2("CARD_REJECTED", [], [], [],
                    new Dictionary<string, string>()) { FailureCode = "CARD_SAMPLES_INVALID" });
            var adjusted = load.Select(v => checked(v * scale + offset)).ToArray();
            var peak = adjusted.Max();
            peakLoad = peakLoad is null ? peak : Math.Max(peakLoad.Value, peak);
            calibrated.Add(new
            {
                id = card.GetProperty("id").GetString(), kind = card.GetProperty("kind").GetString(),
                representation = card.GetProperty("representation").GetString(),
                capturedAtUtc = captureText, source = card.GetProperty("source").GetString(),
                positionUnit = card.GetProperty("positionUnit").GetString(),
                loadUnit = card.GetProperty("loadUnit").GetString(), position, load = adjusted,
                calculationMethod = "LinearLoadCalibration"
            });
        }
        return ValueTask.FromResult(new LogicModuleExecutionResultV2("CARD_CALIBRATION_COMPLETED",
            [new("calibrated-cards", cards.AssetId, "CalibratedCards",
                new("sample.calibrated-cards/v1", JsonSerializer.SerializeToElement(calibrated)), null),
             new("peak-load", cards.AssetId, "PeakLoad",
                new("sample.peak-load/v1", JsonSerializer.SerializeToElement(peakLoad)), "lbf")],
            [], [], new Dictionary<string, string>()));
    }
}
