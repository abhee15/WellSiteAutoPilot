using System.Text.RegularExpressions;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>One current-value operation per catalogued surveillance quantity, so the registry and the catalogue cannot drift.</summary>
internal static partial class ForeSiteSurveillanceOperations
{
    public static IReadOnlyList<AdapterOperationDescriptor> All { get; } =
        [.. ForeSiteSurveillanceCatalogue.All.Select(Describe)];

    public static IReadOnlyList<string> Keys { get; } = [.. All.Select(operation => operation.Key)];

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
