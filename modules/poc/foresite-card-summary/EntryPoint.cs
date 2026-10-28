using System.Globalization;
using System.Text.Json;
using WellSiteAutoPilot.ModuleSdk;

namespace Weatherford.ForeSiteCardSummary;

// POC read-only module: summarises the latest ForeSite surface cards. No recommendations or commands.
public sealed class EntryPoint : ILogicModuleV2
{
    public LogicModuleIdentity Identity { get; } = new("weatherford.foresite-card-summary.dotnet", "1.0.0");

    public ValueTask<LogicModuleExecutionResultV2> ExecuteAsync(LogicModuleExecutionContextV2 context,
        CancellationToken cancellationToken = default)
    {
        var cards = context.Inputs.Single(i => i.RequirementId == "cards");
        var current = default(JsonElement);
        var found = false;
        var newest = DateTimeOffset.MinValue;
        var count = 0;
        foreach (var card in cards.Value.Data.EnumerateArray())
        {
            count++;
            var captured = DateTimeOffset.Parse(card.GetProperty("capturedAtUtc").GetString()!, CultureInfo.InvariantCulture);
            if (captured > newest) newest = captured;
            if (card.GetProperty("kind").GetString() == "CURRENT") { current = card; found = true; }
        }
        if (!found || card_lengths_mismatch(current))
            return ValueTask.FromResult(new LogicModuleExecutionResultV2("CARD_REJECTED", [], [], [],
                new Dictionary<string, string>()) { FailureCode = "CARD_SAMPLES_INVALID" });
        var load = current.GetProperty("load").EnumerateArray().Select(v => v.GetDecimal()).ToArray();
        var position = current.GetProperty("position").EnumerateArray().Select(v => v.GetDecimal()).ToArray();
        var ageHours = Math.Round((decimal)(context.EvaluationTimeUtc - newest).TotalHours, 1);
        StructuredResultValue Number(string id, string quantity, decimal value, string unit) =>
            new(id, cards.AssetId, quantity, new("foresite.number/v1", JsonSerializer.SerializeToElement(value)), unit);
        return ValueTask.FromResult(new LogicModuleExecutionResultV2("CARD_SUMMARY_COMPLETED",
            [Number("card-count", "CardCount", count, "count"),
             Number("peak-load", "PeakLoad", load.Max(), "lb"),
             Number("minimum-load", "MinimumLoad", load.Min(), "lb"),
             Number("stroke-travel", "StrokeTravel", position.Max() - position.Min(), "in"),
             Number("newest-card-age", "NewestCardAge", ageHours, "h")],
            [], [], new Dictionary<string, string>()));
    }

    private static bool card_lengths_mismatch(JsonElement card) =>
        card.GetProperty("position").GetArrayLength() != card.GetProperty("load").GetArrayLength();
}
