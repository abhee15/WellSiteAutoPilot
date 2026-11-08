using System.Net;
using System.Text;
using WellSiteAutoPilot.Integration.ForeSite;

internal static class ApplicableQuantityChecks
{
    // Recorded from the POC for well 776. These are provider-native WellQuantity IDs, not authoring keys.
    private const string RecordedIds = "[2,15,5,19,16,4,6,7,21,20,10,8,9,17,18,3,1,13,14,11,12,22,34,28,32,31,30,71,70,69,23,24]";

    public static async Task RunAsync()
    {
        var handler = new Handler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://foresite.test/prefix/") };
        var session = new ForeSiteSession(client);
        var applicable = await ForeSiteApplicableQuantities.ReadAsync(session, "776", CancellationToken.None);

        Check(applicable.AssetExternalId == "776" && applicable.EnumIds.Count == 32 &&
            applicable.ContainsNativeId(15) && applicable.ContainsNativeId(16) && !applicable.ContainsNativeId(999),
            "POC numeric applicability set was not preserved faithfully");
        Check(handler.Paths.SequenceEqual(["/prefix/TokenService/CreateToken", "/prefix/Surveillance/AvailableQuantities/776"]) &&
            handler.Methods.All(method => method == HttpMethod.Get) && handler.Tokens == 1,
            "Applicability bypassed the authenticated session, escaped the prefix or attempted a write");

        handler.ExpireOnce = true;
        await ForeSiteApplicableQuantities.ReadAsync(session, "776", CancellationToken.None);
        Check(handler.Tokens == 2 && handler.Paths.TakeLast(3).SequenceEqual([
            "/prefix/Surveillance/AvailableQuantities/776", "/prefix/TokenService/CreateToken",
            "/prefix/Surveillance/AvailableQuantities/776"
        ]), "The session did not refresh once and retry after an expired cookie");

        handler.Payload = "[]";
        Check((await ForeSiteApplicableQuantities.ReadAsync(session, "776", CancellationToken.None)).EnumIds.Count == 0,
            "A legitimate empty applicability set must not invent capabilities");

        foreach (var payload in new[]
        {
            "{}", "<html>login</html>", "[0]", "[-1]", "[65536]", "[15,15]",
            "[15.5]", """["15"]""", "[true]", "[null]", "[1,2,]", "[" + string.Join(",", Enumerable.Range(1, 257)) + "]"
        })
        {
            handler.Payload = payload;
            await RejectAsync(session, "776", "FORESITE_APPLICABILITY_RESPONSE_INVALID");
        }

        handler.Payload = new string(' ', ForeSiteApplicableQuantities.MaximumSourceBytes + 1);
        await RejectAsync(session, "776", "FORESITE_APPLICABILITY_TOO_LARGE");
        handler.Chunked = true;
        await RejectAsync(session, "776", "FORESITE_APPLICABILITY_TOO_LARGE");
        handler.Chunked = false;

        handler.Payload = RecordedIds;
        foreach (var invalidId in new[] { "0", "-1", "abc", "776/../42", "", "9223372036854775808" })
            await RejectAsync(session, invalidId, "FORESITE_WELL_ID_INVALID");

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        try
        {
            await ForeSiteApplicableQuantities.ReadAsync(session, "776", canceled.Token);
            throw new InvalidOperationException("Canceled applicability read was accepted.");
        }
        catch (OperationCanceledException) { }

        Console.WriteLine("ForeSite applicable quantities: POC IDs, authentication, 401 refresh, scope, bounds, invalid responses and cancellation passed.");
    }

    private static async Task RejectAsync(ForeSiteSession session, string externalId, string expectedCode)
    {
        try
        {
            await ForeSiteApplicableQuantities.ReadAsync(session, externalId, CancellationToken.None);
        }
        catch (ForeSiteMeasurementException error) when (error.Code == expectedCode) { return; }

        throw new InvalidOperationException("ForeSite applicability accepted invalid data: " + expectedCode);
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string Payload { get; set; } = RecordedIds;
        public int Tokens { get; private set; }
        public bool ExpireOnce { get; set; }
        public bool Chunked { get; set; }
        public List<string> Paths { get; } = [];
        public List<HttpMethod> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Paths.Add(request.RequestUri!.AbsolutePath);
            Methods.Add(request.Method);
            if (request.RequestUri.AbsolutePath.EndsWith("/TokenService/CreateToken", StringComparison.Ordinal))
            {
                var token = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
                token.Headers.TryAddWithoutValidation("Set-Cookie", "session=" + (++Tokens) + "; Path=/prefix");
                return Task.FromResult(token);
            }
            Check(request.RequestUri.AbsolutePath == "/prefix/Surveillance/AvailableQuantities/776", "Unexpected provider route.");
            Check(request.Headers.TryGetValues("Cookie", out var values) &&
                values.Single().Contains("session=" + Tokens, StringComparison.Ordinal), "Session cookie missing or stale.");
            if (ExpireOnce)
            {
                ExpireOnce = false;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Chunked ? new UnknownLengthContent(Payload) : new StringContent(Payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class UnknownLengthContent(string body) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(body)));
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(Encoding.UTF8.GetBytes(body)).AsTask();
    }
}
