using System.Text.Json;

namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>Synthetic examples serialized from the same typed results used by providers, never live source evidence.</summary>
public static class AdapterReferenceExamples
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static readonly DateTimeOffset ExampleTime = DateTimeOffset.Parse("2026-01-15T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    public const string AdapterBoundary = "Typed adapter interface inside the Integration Gateway. The runtime supplies authorized Connection/Asset context; modules never receive provider settings or credentials.";

    public static AdapterOperationDocumentation Current(string externalId, string quantity, string unit, string source, string quality,
        string? timestampKind) => new(
        $"// Inside the Integration Gateway: ICurrentDataProvider provider\nvar result = await provider.GetCurrentAsync(\"{externalId}\", \"{quantity}\", cancellationToken);",
        AdapterBoundary + " Hosted modules declare supported current inputs and receive them in their execution context.",
        Serialize(new { externalId, quantity }),
        Serialize(new EngineeringValue(quantity, 62.5m, unit, ExampleTime, quality, source, ExampleTime.AddSeconds(2), timestampKind)));

    public static AdapterOperationDocumentation History(string externalId, string quantity, string unit, string source, string quality) => new(
        "// Inside the Integration Gateway: IBoundedHistoricalDataProvider provider\nvar request = new HistoricalReadRequest(assetExternalId, quantity, startUtc, endUtc, maximumSamples);\nvar result = await provider.ReadHistoryAsync(request, cancellationToken);",
        AdapterBoundary + " This history contract is implemented at the adapter boundary; hosted-module history binding is not currently exposed.",
        Serialize(new HistoricalReadRequest(externalId, quantity, ExampleTime, ExampleTime.AddHours(1), 100)),
        Serialize(new HistoricalReadResult(quantity, source, null, "Raw", ExampleTime, ExampleTime.AddHours(1),
            [new(quantity, 62.5m, unit, ExampleTime.AddMinutes(5), quality, source, ExampleTime.AddHours(1), "HistorianSample")], false, [])));

    public static AdapterOperationDocumentation StructuredCurrent(string externalId, string quantity, EngineeringValue response) => new(
        $"// Inside the Integration Gateway: ICurrentDataProvider provider\nvar result = await provider.GetCurrentAsync(\"{externalId}\", \"{quantity}\", cancellationToken);",
        AdapterBoundary + " Hosted structured-card inputs require the reviewed V2 manifest, exact schema and an explicit policy accepting source quality.",
        Serialize(new { externalId, quantity }), Serialize(response));

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
}
