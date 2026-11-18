using System.Net;
using System.Text;
using WellSiteAutoPilot.Integration.ForeSite;

internal static class WellTestChecks
{
    private const string RecordedShape = """
        {"Id":7,"WellId":101,"SampleDate":"2025-02-01T10:00:00-06:00","TestEndDate":"2025-02-01T17:00:00Z",
        "Status":null,"SPTCode":9,"SPTCodeDescription":"RejectedTest","ValidationStatusCode":0,
        "ValidationStatus":"Oil rate above acceptance limit","Oil":23,"Water":0,"Gas":null,"TotalFluid":"invalid","StrokePerMinute":1e40,"LastChangedDT":"2025-02-08T17:00:00Z"}
        """;

    public static async Task RunAsync()
    {
        using var client = NewClient(RecordedShape);
        var result = await new ForeSiteLatestWellTestReader(new ForeSiteSession(client)).ReadAsync(new("101"));
        var test = result.Test;
        if (test is null || test.Id != "7" || test.Status is not null || test.Validation.ClassificationCode != "9" ||
            test.Validation.Code != "0" || test.Validation.ClassificationDescription != "RejectedTest" ||
            test.SampleDate?.Offset != TimeSpan.FromHours(-6) || test.TestEndDate?.Hour != 17 ||
            result.AssetExternalId != "101" || result.RetrievedAtUtc <= test.TestEndDate)
            throw new InvalidOperationException("Well test metadata changed provider identity, timestamps or status semantics.");

        if (test.ReportedQuantityFields is not { } fields || !fields.SequenceEqual(["Oil", "Water"]))
            throw new InvalidOperationException("Reported quantities must retain zero and exclude null, malformed and metadata fields.");

        using var empty = NewClient("null");
        if ((await new ForeSiteLatestWellTestReader(new ForeSiteSession(empty)).ReadAsync(new("101"))).Test is not null)
            throw new InvalidOperationException("Null test response must remain empty.");

        using var noBody = NewClient("");
        if ((await new ForeSiteLatestWellTestReader(new ForeSiteSession(noBody)).ReadAsync(new("101"))).Test is not null)
            throw new InvalidOperationException("An empty 200 body means no reported test, not an invalid response.");

        await Refuses(RecordedShape.Replace("\"WellId\":101", "\"WellId\":102", StringComparison.Ordinal), "FORESITE_WELL_TEST_IDENTITY_MISMATCH");
        await Refuses(RecordedShape.Replace("\"Id\":7", "\"Id\":7,\"id\":8", StringComparison.Ordinal), "FORESITE_WELL_TEST_RESPONSE_INVALID");
        await Refuses(RecordedShape.Replace("-06:00", "", StringComparison.Ordinal), "FORESITE_WELL_TEST_TIMESTAMP_INVALID");
        await Refuses("[" + RecordedShape + "]", "FORESITE_WELL_TEST_RESPONSE_INVALID");
        await Refuses(new string(' ', 1048577), "FORESITE_PAYLOAD_TOO_LARGE");
        Console.WriteLine("ForeSite latest well test: recorded shape, independent statuses, identity, date offsets, empty test and response limits passed.");
    }

    private static async Task Refuses(string json, string code)
    {
        using var client = NewClient(json);
        try { await new ForeSiteLatestWellTestReader(new ForeSiteSession(client)).ReadAsync(new("101")); }
        catch (ForeSiteMeasurementException ex) when (ex.Code == code) { return; }
        throw new InvalidOperationException("Expected " + code);
    }

    private static HttpClient NewClient(string json) =>
        new(new Fixture(json)) { BaseAddress = new Uri("http://foresite.test/prefix/") };

    private sealed class Fixture(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("Well Test read must be GET.");
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/prefix/TokenService/CreateToken" => new HttpResponseMessage(HttpStatusCode.OK),
                "/prefix/WellTests/GetLatestWellTestData/101" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                },
                _ => throw new InvalidOperationException("Unexpected ForeSite path.")
            });
        }
    }
}
