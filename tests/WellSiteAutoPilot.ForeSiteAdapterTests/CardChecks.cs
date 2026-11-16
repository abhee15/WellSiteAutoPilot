using System.Net;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.ForeSite;

internal static class CardChecks
{
    private const string Valid = """{"Value":{"Timestamp":"2022-10-01T12:00:00Z","SurfaceCards":[{"Points":[[0,100],[2,200]]},{"Points":[[0,10],[1,20],[2,30]]}]},"Units":{"Position":{"UnitKey":"in"},"Load":{"UnitKey":"lb"}}}""";
    private const string NoCards = """{"Value":null,"ErrorMessage":"There were no dynacards found for well 42."}""";
    public static async Task RunAsync()
    {
        var handler = new Handler(Valid);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fixture/prefix/") };
        var result = await ForeSiteCards.ReadAsync(new(client), "42", CancellationToken.None);
        if (result.Value is not null || result.Quality != "Unknown" || result.TimestampKind != "CardCapture" ||
            result.TimestampUtc.Year != 2022 || result.StructuredValue?.SchemaId != ForeSiteCards.SchemaId || !MeasurementPayloadLimits.IsValid(result))
            throw new InvalidOperationException("Stored cards invented scalar data, freshness or quality.");
        var cards = result.StructuredValue.Data;
        if (cards.GetArrayLength() != 4 || cards[1].GetProperty("load").GetArrayLength() != 3 ||
            cards[2].GetProperty("kind").GetString() != "FULL" || cards[0].GetProperty("load")[1].GetDecimal() != 200 ||
            cards[0].GetProperty("loadUnit").GetString() != "lb" || handler.Reads != 2)
            throw new InvalidOperationException("Card identity, pairing or collection shape was lost.");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var roundTrip = JsonSerializer.Deserialize<EngineeringValue>(JsonSerializer.Serialize(result, options), options)!;
        if (roundTrip.StructuredValue!.Data.GetRawText() != cards.GetRawText() || roundTrip.Value is not null)
            throw new InvalidOperationException("Structured transport lost card evidence.");
        foreach (var (payload, code) in new[]
        {
            (Valid.Replace("2022-10-01T12:00:00Z", "2022-10-01T12:00:00", StringComparison.Ordinal), "FORESITE_TIMESTAMP_INVALID"),
            (Valid.Replace("[0,100]", "[0]", StringComparison.Ordinal), "FORESITE_CARD_POINTS_INVALID"),
            (Valid.Replace("[0,100]", "[0,\"secret\"]", StringComparison.Ordinal), "FORESITE_CARD_POINTS_INVALID"),
            (Valid.Replace("\"lb\"", "\"unknown\"", StringComparison.Ordinal), "FORESITE_UNIT_UNSUPPORTED"),
            (Valid.Replace("\"Value\":", "\"FullErrorMessage\":\"secret\",\"Value\":", StringComparison.Ordinal), "FORESITE_RESPONSE_INVALID"),
            (Valid.Replace("\"Timestamp\":", "\"Timestamp\":null,\"Timestamp\":", StringComparison.Ordinal), "FORESITE_RESPONSE_INVALID"),
            ("<html>secret</html>", "FORESITE_RESPONSE_INVALID"),
            (new string('x', MeasurementPayloadLimits.MaximumReplyBytes + 1), "FORESITE_PAYLOAD_TOO_LARGE")
        })
        {
            using var invalidClient = new HttpClient(new Handler(payload)) { BaseAddress = new Uri("http://fixture/prefix/") };
            try { await ForeSiteCards.ReadAsync(new(invalidClient), "42", CancellationToken.None); }
            catch (ForeSiteMeasurementException error) when (error.Code == code) { continue; }
            throw new InvalidOperationException("Malformed card did not fail with " + code);
        }
        await NoStoredCardsAsync();
        var oversized = new CountingStream(new byte[MeasurementPayloadLimits.MaximumReplyBytes * 2]);
        using var streamingClient = new HttpClient(new Handler(Valid, () => new UnboundedContent(oversized)))
            { BaseAddress = new Uri("http://fixture/prefix/") };
        try
        {
            await ForeSiteCards.ReadAsync(new(streamingClient), "42", CancellationToken.None);
            throw new InvalidOperationException("Chunked card response bypassed the byte limit.");
        }
        catch (ForeSiteMeasurementException error) when (error.Code == "FORESITE_PAYLOAD_TOO_LARGE")
        {
            if (oversized.BytesRead != MeasurementPayloadLimits.MaximumReplyBytes + 1 || !oversized.Disposed)
                throw new InvalidOperationException("Card response was buffered beyond the limit or not disposed.");
        }
        Console.WriteLine("ForeSite stored-card contract and malformed-input checks passed.");
    }
    private static async Task NoStoredCardsAsync()
    {
        // No stored cards of either type is an empty library (the host shows "no stored surface cards"), not an invalid reply.
        var empty = new Handler(NoCards);
        using var emptyClient = new HttpClient(empty) { BaseAddress = new Uri("http://fixture/prefix/") };
        try
        {
            await ForeSiteCards.ReadAsync(new(emptyClient), "42", CancellationToken.None);
            throw new InvalidOperationException("A well with no stored cards returned card data.");
        }
        catch (ForeSiteMeasurementException error) when (error.Code == "FORESITE_MEASUREMENT_MISSING")
        {
            if (empty.Reads != 2) throw new InvalidOperationException("Both card types must be checked before reporting no cards.");
        }
        // One type empty and the other populated returns the populated cards only.
        using var mixedClient = new HttpClient(new Handler(Valid, byRead: read => read == 1 ? NoCards : Valid))
            { BaseAddress = new Uri("http://fixture/prefix/") };
        var mixed = await ForeSiteCards.ReadAsync(new(mixedClient), "42", CancellationToken.None);
        if (mixed.StructuredValue!.Data.GetArrayLength() != 2 ||
            mixed.StructuredValue.Data[0].GetProperty("kind").GetString() != "FULL")
            throw new InvalidOperationException("An empty CURRENT library hid the FULL cards.");
        // Only the documented no-cards reply is tolerated; other null Values and errors stay invalid.
        foreach (var payload in new[]
        {
            """{"Value":null}""",
            """{"Value":null,"ErrorMessage":"Access denied."}""",
            """{"Value":null,"ErrorMessage":null}""",
            """{"Value":{},"ErrorMessage":"There were no dynacards found."}""",
            """[{"Value":null,"ErrorMessage":"There were no dynacards found."}]"""
        })
        {
            using var invalidClient = new HttpClient(new Handler(payload)) { BaseAddress = new Uri("http://fixture/prefix/") };
            try { await ForeSiteCards.ReadAsync(new(invalidClient), "42", CancellationToken.None); }
            catch (ForeSiteMeasurementException error) when (error.Code == "FORESITE_RESPONSE_INVALID") { continue; }
            throw new InvalidOperationException("A non-empty-library reply was treated as no stored cards: " + payload);
        }
    }
    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public bool Disposed { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += count;
            return count;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private sealed class UnboundedContent(Stream stream) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) =>
            throw new InvalidOperationException("Card response was eagerly buffered.");
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(stream);
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) => Task.FromResult(stream);
    }
    private sealed class Handler(string payload, Func<HttpContent>? contentFactory = null, Func<int, string>? byRead = null) : HttpMessageHandler
    {
        public int Reads { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("Card acquisition issued a write.");
            if (request.RequestUri!.AbsolutePath == "/prefix/TokenService/CreateToken")
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                response.Headers.TryAddWithoutValidation("Set-Cookie", "test=private; Path=/prefix");
                return Task.FromResult(response);
            }
            Reads++;
            if (request.RequestUri.AbsolutePath != $"/prefix/Dynacards/Dynacard/LatestWithUnits/Lib/42/{Reads}" ||
                !request.Headers.Contains("Cookie")) throw new InvalidOperationException("Card read escaped the saved session or route.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = contentFactory?.Invoke() ?? new StringContent(byRead?.Invoke(Reads) ?? payload) });
        }
    }
}
