using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

/// <summary>ForeSite-owned reference metadata for implemented typed reads. All example data is synthetic.</summary>
internal static class ForeSiteReference
{
    public static AdapterOperationDocumentation Current(string quantity) =>
        AdapterReferenceExamples.Current("101", quantity, "%", "foresite:controller-scan", "Good", "ControllerScan") with
        { ProviderReference = "GET Surveillance/well/{wellId}; source field " + ForeSiteMeasurements.SourceField(quantity) + ". Timestamp: LastGoodScanTime." };

    public static AdapterOperationDocumentation Cards()
    {
        var time = AdapterReferenceExamples.ExampleTime;
        var cards = JsonSerializer.SerializeToElement(new[] { new {
            id = "example-current", kind = "CURRENT", representation = "SURFACE", capturedAtUtc = time,
            source = "foresite:dynacard-library", positionUnit = "in", loadUnit = "lb",
            position = new decimal[] { 0, 12, 0 }, load = new decimal[] { 0, 1200, 0 }
        } });
        return AdapterReferenceExamples.StructuredCurrent("101", ForeSiteCards.Quantity,
            new EngineeringValue(ForeSiteCards.Quantity, null, "", time, "Unknown", "foresite:dynacard-library", time.AddSeconds(2), "CardCapture")
            { StructuredValue = new(ForeSiteCards.SchemaId, cards) }) with
        { ProviderReference = "GET Dynacards/Dynacard/LatestWithUnits/Lib/{wellId}/{type}; types 1 (CURRENT) and 2 (FULL). Reads stored surface cards; never initiates a controller scan." };
    }

    public static readonly AdapterOperationDescriptor ConfigurationOperation = new(
        "configuration.well-details", "Read well details", "Read the well's supported identity and classification configuration fields.",
        ProviderCapabilityKind.ConfigurationData, "asset.configuration", "1.0",
        new("asset.configuration-request/v1", [new("assetExternalId", "string", true, "Explicit imported source identity; ForeSite requires a positive numeric well ID.")]),
        new("asset.configuration-result/v1", [
            new("assetExternalId", "string", true, "Exact requested source identity; mismatched replies are refused."),
            new("displayName", "string", true, "Source well name."),
            new("resourceKind", "string", true, "foresite.well."),
            new("details", "object", true, "Supported configuration details only; never a raw provider DTO."),
            new("details.classificationCode", "string|null", false, "Source WellType code; not device type."),
            new("details.facilityId", "string|null", false, "Source FacilityId without inference."),
            new("details.field", "string|null", false, "Source field."),
            new("details.lease", "string|null", false, "Source lease."),
            new("details.deviceType", "string|null", false, "PrincipalDeviceType when supplied; absence remains null."),
            new("sourceChangedAtUtc", "date-time|null", false, "Source ChangeDate with an explicit offset, when supplied."),
            new("retrievedAtUtc", "date-time", true, "Retrieval instant, never substituted for source change time."),
            new("source", "string", true, "foresite:well-details.")]),
        "Source ChangeDate is preserved in UTC when supplied. Missing source time stays null; retrieval time is separate.",
        "Configuration details have no telemetry quality code. No Good quality or controller-scan semantics are inferred.",
        new(1, MeasurementPayloadLimits.MaximumValueBytes, MeasurementPayloadLimits.MaximumReplyBytes, 6, MaximumSourceBytes: 1048576),
        ResourceKind: ForeSiteAdapter.WellResourceKind,
        Documentation: new(
            "// Inside the Integration Gateway: IAssetConfigurationProvider provider\nvar request = new AssetConfigurationReadRequest(assetExternalId);\nvar result = await provider.ReadConfigurationAsync(request, cancellationToken);",
            AdapterReferenceExamples.AdapterBoundary + " Hosted-module configuration binding is not currently exposed. This read does not discover or import Assets.",
            AdapterReferenceExamples.Serialize(new AssetConfigurationReadRequest("101")),
            AdapterReferenceExamples.Serialize(new AssetConfigurationResult("101", "Example well", "foresite.well",
                new("1", "EXAMPLE_FACILITY", "Example field", "Example lease", null), null,
                AdapterReferenceExamples.ExampleTime, "foresite:well-details")),
            "GET Wells/Well/{wellId}; projects the supported WellDTO identity/classification fields only."));
}
