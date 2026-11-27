using System.Text.RegularExpressions;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>One current-value operation per catalogued surveillance quantity, so the registry and the catalogue cannot drift.</summary>
internal static partial class ForeSiteSurveillanceOperations
{
    public static IReadOnlyList<AdapterOperationDescriptor> All { get; } =
        [.. ForeSiteSurveillanceCatalogue.All.Select(Describe)];

    public static IReadOnlyList<string> Keys { get; } = [.. All.Select(operation => operation.Key)];

    /// <summary>The same catalogue as a time range: whatever can be read now can be read over a window, when the well historizes it.</summary>
    public static IReadOnlyList<AdapterOperationDescriptor> History { get; } =
        [.. ForeSiteSurveillanceCatalogue.All.Select(DescribeHistory)];

    public static IReadOnlyList<string> HistoryKeys { get; } = [.. History.Select(operation => operation.Key)];

    private static AdapterOperationDescriptor DescribeHistory(ForeSiteSurveillanceQuantity item) =>
        HistoricalReadLimits.Operation("history." + Kebab(item.Quantity), item.Quantity, ForeSiteAdapter.WellResourceKind, 6,
            "Source timestamps require an explicit UTC offset. Reads use [start, end) UTC instants with whole-second request bounds; fractional bounds are refused.",
            "Trend responses do not supply quality; returned quality is Unknown. A well that does not historize this field fails the read.") with
        {
            Documentation = AdapterReferenceExamples.History("101", item.Quantity, item.Unit, "foresite:trends", "Unknown") with
            { ProviderReference = "POST Surveillance/CygNetTrends/{wellId}/{start}/{end}; requests " + item.Field + ". Whole-second UTC bounds, no implicit aggregation." }
        };

    private static AdapterOperationDescriptor Describe(ForeSiteSurveillanceQuantity item) =>
        EngineeringOperationSchemas.Current("current." + Kebab(item.Quantity), Label(item.Quantity),
            "Controller surveillance value for " + string.Join(", ", item.WellTypes) + " wells.",
            item.Quantity, ForeSiteAdapter.WellResourceKind, item.Unit,
            "LastGoodScanTime: successful controller scan, never the latest attempted scan.",
            "Good indicates successful acquisition of a valid controller value, not independent sensor validation.", 6) with
        { Documentation = ForeSiteReference.Current(item.Quantity) };

    private static string Words(string name) => Boundary().Replace(name, " $1");

    internal static string Label(string quantity)
    {
        var words = Words(quantity);
        return words[..1] + words[1..].ToLowerInvariant();
    }

    internal static string Kebab(string quantity) => Words(quantity).ToLowerInvariant().Replace(' ', '-');

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])|(?<=[A-Z])([A-Z][a-z])")]
    private static partial Regex Boundary();
}
