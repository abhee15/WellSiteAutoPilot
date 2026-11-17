using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.ForeSite;

public static class ForeSiteWellTestReference
{
    public static readonly AdapterOperationDescriptor Latest = new(
        ForeSiteLatestWellTestContract.OperationKey, "Latest reported well test",
        "Read the latest test record identity, dates, reported status and recognized numeric fields for one well.",
        ProviderCapabilityKind.ExtensionRead, "well-test.latest-reported", ForeSiteLatestWellTestContract.Version,
        new("well-test.latest-request/v1", [new("assetExternalId", "string", true, "Imported ForeSite well ID.")]),
        new("well-test.latest-reported/v1", [
            new("assetExternalId", "string", true, "Imported ForeSite well ID."),
            new("test", "object|null", true, "Identity, sample and end times, status and validation fields."),
            new("test.id", "string", true, "ForeSite test record ID."),
            new("test.sampleDate", "date-time|null", false, "Source sample date, with original offset."),
            new("test.testEndDate", "date-time|null", false, "Source test end date, with original offset."),
            new("test.status", "string|null", false, "Provider test status when supplied."),
            new("test.validation", "object", true, "Provider classification and validation codes and descriptions."),
            new("test.reportedQuantityFields", "string[]", false, "Recognized source fields with numeric values; conveys no eligibility or units."),
            new("retrievedAtUtc", "date-time", true, "Time of retrieval."),
            new("source", "string", true, "Provider provenance.")]),
        "SampleDate and TestEndDate are independent source fields. Retrieval time is separate.",
        "Status and validation fields reflect ForeSite's report. A returned test may be rejected.",
        new(1, 4096, 4096, TimeoutSeconds: 6, MaximumSourceBytes: ForeSiteLatestWellTestReader.MaximumSourceBytes),
        ResourceKind: ForeSiteAdapter.WellResourceKind,
        Documentation: new("well-test.latest-reported", "Server-authorized read through the Integration Gateway.",
            """{"assetExternalId":"101"}""",
            """{"assetExternalId":"101","test":{"id":"123","sampleDate":"2024-09-16T05:00:00Z","testEndDate":"2024-09-17T05:00:00Z","status":null,"validation":{"classificationCode":"9","classificationDescription":"RejectedTest","code":"0","description":"Oil rate above acceptance limit"}},"retrievedAtUtc":"2026-11-14T00:00:00Z","source":"foresite:well-tests"}""",
            "GET WellTests/GetLatestWellTestData/{wellId}"));
}
