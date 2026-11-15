using System.Text.Json.Serialization;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

public sealed record CygNetPointCatalogRequest([property: JsonRequired] string AssetExternalId);
public sealed record CygNetPointCatalogEntry(string Key, string Udc, string? Description, string? Unit);
public sealed record CygNetPointCatalogResult(string AssetExternalId, IReadOnlyList<CygNetPointCatalogEntry> Points, bool Truncated);

public sealed record CygNetPointMetadata(string Udc, string? Description, string? Unit);
public sealed record CygNetPointList(string FacilityTag, IReadOnlyList<CygNetPointMetadata> Points, bool Truncated);

/// <summary>Optional, facility-scoped PNT metadata discovery. It does not read point values.</summary>
public interface ICygNetPointCatalogClient
{
    Task<CygNetPointList> ListPointsAsync(CygNetConnection connection, string facilityId, CancellationToken cancellationToken);
}

public static class CygNetPointCatalog
{
    public const int MaximumPoints = 250;
    private const string GroupKey = "point-catalog";

    public static readonly AdapterOperationDescriptor Operation = new(
        "catalog.facility-points", "Facility points", "List the point metadata reported for one CygNet facility.",
        ProviderCapabilityKind.ExtensionRead, "cygnet.point-catalog", "1.0",
        new("cygnet.point-catalog-request/v1", [new("assetExternalId", "string", true, "Imported CygNet facility tag.")]),
        new("cygnet.point-catalog-result/v1", [
            new("assetExternalId", "string", true, "Exact requested facility tag."),
            new("points", "object[]", true, "Discovered point metadata."),
            new("points[].key", "string", true, "Catalog key in the cygnet.udc namespace."),
            new("points[].udc", "string", true, "CygNet Uniform Data Code."),
            new("points[].description", "string|null", false, "Point description when supplied."),
            new("points[].unit", "string|null", false, "Point PrimaryUnits as reported. Missing stays null."),
            new("truncated", "boolean", true, "True when the facility has more points than the bounded result.")]),
        "Point configuration has no sample timestamp.", "Point presence is not evidence of value, quality, or read authorization.",
        new(1, MeasurementPayloadLimits.MaximumValueBytes, MeasurementPayloadLimits.MaximumReplyBytes, TimeoutSeconds: 15, MaximumSamples: MaximumPoints),
        ResourceKind: CygNetAdapter.ResourceKind,
        Documentation: new("catalog.facility-points", "Server-authorized metadata read through the Integration Gateway.",
            """{"assetExternalId":"SITE.UIS::EXAMPLE_WELL"}""",
            """{"assetExternalId":"SITE.UIS::EXAMPLE_WELL","points":[{"key":"cygnet.udc.PRCASXIN","udc":"PRCASXIN","description":"Casing Pressure","unit":"psi"}],"truncated":false}""",
            "CygNet PNT ConfigClient.GetPointTagList, scoped to site.service and facility, then ReadPointRecord."));

    internal static AdapterDescriptor Describe(AdapterDescriptor descriptor) => descriptor with
    {
        Capabilities = [.. descriptor.Capabilities, new(ProviderCapabilityKind.ExtensionRead, "Point catalog")],
        Operations = [.. descriptor.Operations ?? [], Operation],
        OperationGroups = [.. descriptor.OperationGroups ?? [],
            new(GroupKey, "Point catalog", "Facility point metadata and reported engineering units.", "1.0", [Operation.Key])]
    };

    public static async Task<CygNetPointCatalogResult> ReadAsync(ICygNetPointCatalogClient client, CygNetConnection connection,
        CygNetPointCatalogRequest request, CancellationToken cancellationToken)
    {
        var externalId = request.AssetExternalId;
        var facility = ScopedFacility(connection, externalId);

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await client.ListPointsAsync(connection, facility, cancellationToken);
            if (!string.Equals(result.FacilityTag, externalId, StringComparison.OrdinalIgnoreCase) || result.Points.Count > MaximumPoints)
                throw InvalidResult();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<CygNetPointCatalogEntry>(result.Points.Count);
            foreach (var point in result.Points)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsUdc(point.Udc) || !seen.Add(point.Udc) || !ValidText(point.Description, 96) || !ValidText(point.Unit, 32))
                    throw InvalidResult();
                entries.Add(new("cygnet.udc." + point.Udc.ToUpperInvariant(), point.Udc, point.Description, point.Unit));
            }
            entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
            return new(externalId, entries, result.Truncated);
        }
        catch (CygNetReadException error) { throw Failure(error.Code); }
        catch (CygNetUnavailableException) { throw Failure("CYGNET_UNAVAILABLE"); }
        catch (UnauthorizedAccessException) { throw Failure("CYGNET_CATALOG_FORBIDDEN"); }
    }

    internal static string ScopedFacility(CygNetConnection connection, string? externalId)
    {
        var prefix = connection.SiteService + "::";
        if (externalId is null || externalId.Length > CygNetAdapter.MaximumFacilityTagLength ||
            !externalId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw InvalidFacility();

        var facility = externalId[prefix.Length..];
        if (!IsFacilityId(facility) || connection.FacilityFilter is { } filter && !CygNetAdapter.Matches(filter, facility))
            throw InvalidFacility();

        return facility;
    }

    public static bool IsFacilityId(string facility) =>
        !string.IsNullOrWhiteSpace(facility) && facility.AsSpan().Trim().Length == facility.Length &&
        !facility.Any(character => character is ':' or '/' or '\\' or '*' or '?' || char.IsControl(character));

    public static bool IsUdc(string? udc) => udc is { Length: > 0 and <= 32 } &&
        udc.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static bool ValidText(string? text, int maxLength) => text is null ||
        text.Length <= maxLength && !text.Any(char.IsControl);

    private static WellSiteAutoPilotException InvalidFacility() =>
        new("CYGNET_FACILITY_INVALID", FailureKind.Validation, "The facility tag does not match the saved CygNet connection.");

    private static WellSiteAutoPilotException InvalidResult() => Failure("CYGNET_CATALOG_RESPONSE_INVALID");

    private static WellSiteAutoPilotException Failure(string code) =>
        new(code, FailureKind.DependencyUnavailable, "The selected CygNet connection could not provide the point catalog.");
}
