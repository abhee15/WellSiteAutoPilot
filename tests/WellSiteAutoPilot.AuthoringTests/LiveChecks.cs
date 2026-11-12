using System.Net;
using System.Text;
using System.Text.Json;

namespace WellSiteAutoPilot.AuthoringTests;

/// <summary>
/// Opt-in (WSA_LIVE_AUTHORING=1): drives the running Server's authoring API against the live ForeSite and CygNet
/// connections, then cross-checks the numbers against ForeSite read directly as the current Windows identity.
/// Only the box with access to those systems can run it.
/// </summary>
internal static class LiveChecks
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Guid ForeSite = Guid.Parse(Environment.GetEnvironmentVariable("WSA_LIVE_FORESITE_CONNECTION") ?? "b07a6af6-14d6-40bf-a774-6df4986ac9fc");
    private static readonly Guid CygNet = Guid.Parse(Environment.GetEnvironmentVariable("WSA_LIVE_CYGNET_CONNECTION") ?? "d7f6312d-38ed-4a3c-ae3a-6fa3772647ba");

    public static async Task RunAsync()
    {
        if (Environment.GetEnvironmentVariable("WSA_LIVE_AUTHORING") != "1")
        {
            Console.WriteLine("Live authoring checks skipped (set WSA_LIVE_AUTHORING=1 on a box with ForeSite and CygNet).");
            return;
        }
        var api = new Uri((Environment.GetEnvironmentVariable("WSA_API") ?? "http://localhost:5080/api/v1").TrimEnd('/') + "/");
        using var http = new HttpClient { BaseAddress = api, Timeout = TimeSpan.FromSeconds(150) };
        http.DefaultRequestHeaders.Add("X-WSA-Test-Identity", @"LOCAL\wsa-dev-operator");

        var starters = (await Get(http, "authoring/starters")).EnumerateArray().ToDictionary(s => s.GetProperty("id").GetString()!);
        JsonElement Draft(string id) => starters[id].GetProperty("draft");

        var invalid = await Post(http, "authoring/validate", new { name = "x", inputs = Array.Empty<object>(), parameters = Array.Empty<object>(), steps = Array.Empty<object>(), outputs = Array.Empty<object>() });
        Check.That(!invalid.GetProperty("valid").GetBoolean(), "the server validates an empty draft as invalid");

        // Well 1596 (AR-RRL1, AEBEAM): surveillance has no casing, so the fallback must reach the latest well test.
        var wellTest = await ForeSiteDirect.GetAsync("WellTests/GetLatestWellTestData/1596");
        var expectedCasing = ForeSiteDirect.Number(wellTest, "AverageCasingPressure");
        var expectedTubing = ForeSiteDirect.Number(wellTest, "AverageTubingPressure");
        var casing1596 = await Evaluate(http, Draft("fallback"), ForeSite, "1596");
        Check.That(Outcome(casing1596) == "Runs", "1596 casing fallback runs");
        var casingInput = casing1596.GetProperty("inputs")[0];
        Check.That(casingInput.GetProperty("basis").GetString() == "TestSample", "1596 casing comes from the well test, not surveillance");
        Check.That(Math.Abs(Out(casing1596, "Casing_out") - expectedCasing) < 0.001, $"1596 casing {Out(casing1596, "Casing_out")} equals ForeSite's well test {expectedCasing}");
        Check.That(casingInput.GetProperty("reasons").GetArrayLength() >= 1, "the trace explains why surveillance did not answer");

        var diff1596 = await Evaluate(http, Draft("differential"), ForeSite, "1596");
        Check.That(Outcome(diff1596) == "Runs" && Math.Abs(Out(diff1596, "Differential_out") - (expectedCasing - expectedTubing)) < 0.001,
            "1596 differential equals casing minus tubing from the well test");

        var snapshot1596 = await Evaluate(http, Draft("snapshot"), ForeSite, "1596");
        Check.That(Outcome(snapshot1596) == "Skipped", "1596 has no surveillance casing, so the surveillance-only snapshot is skipped, not zero");

        var location1596 = await Evaluate(http, Draft("location"), ForeSite, "1596");
        var well1596 = await ForeSiteDirect.GetAsync("Wells/Well/1596");
        Check.That(Outcome(location1596) == "Runs" && Math.Abs(Out(location1596, "Latitude_out") - ForeSiteDirect.Number(well1596, "SurfaceLatitude")) < 1e-6
            && Math.Abs(Out(location1596, "Longitude_out") - ForeSiteDirect.Number(well1596, "SurfaceLongitude")) < 1e-6, "1596 location equals ForeSite's well record");

        // Well 776 (RPOC): live surveillance has casing, tubing and fillage; there is no well test.
        var surveillance = await ForeSiteDirect.GetAsync("Surveillance/well/776");
        var snapshot776 = await Evaluate(http, Draft("snapshot"), ForeSite, "776");
        Check.That(Outcome(snapshot776) == "Runs", "776 snapshot runs");
        var casing776 = Out(snapshot776, "Casing_out");
        var directCasing = ForeSiteDirect.Number(surveillance, "CasingPressure");
        Check.That(casing776 is >= 0 and <= 3000, $"776 casing {casing776} is a plausible pressure");
        Check.That(Math.Abs(casing776 - directCasing) <= Math.Max(5, directCasing * 0.1), $"776 casing {casing776} agrees with ForeSite read directly ({directCasing})");
        var directUnit = ForeSiteDirect.Unit(surveillance, "CasingPressure");
        Check.That(snapshot776.GetProperty("inputs")[0].GetProperty("unit").GetString() == directUnit && directUnit.Length > 0,
            $"776 casing carries the unit ForeSite reports ({directUnit})");

        var threshold776 = await Evaluate(http, Draft("threshold"), ForeSite, "776");
        Check.That(Outcome(threshold776) == "Runs", "776 threshold falls back to a source and runs");
        var status = threshold776.GetProperty("outputs").EnumerateArray().Single(o => o.GetProperty("name").GetString() == "Status_out").GetProperty("value").GetProperty("text").GetString();
        Check.That(status == (casing776 > 250 ? "High" : "Normal") || Math.Abs(casing776 - 250) < 25, "776 status follows the live casing and the 250 psi limit");

        var plan776 = await Evaluate(http, Draft("setpoint"), ForeSite, "776");
        Check.That(Outcome(plan776) == "Runs", "776 set point module runs");
        var change = plan776.GetProperty("outputs")[0];
        if (change.GetProperty("value").GetProperty("number").GetDouble() > 0)
        {
            var plan = change.GetProperty("plan");
            Check.That(plan.GetProperty("allowed").GetBoolean() && plan.GetProperty("setPoint").GetString() == "IdealPF", "776 IdealPF change is allowed");
            Check.That(Math.Abs(plan.GetProperty("proposed").GetDouble() - plan.GetProperty("current").GetDouble() - 1) < 1e-9, "the plan proposes current + 1");
            Check.That(change.GetProperty("note").GetString()!.Contains("Nothing was sent", StringComparison.Ordinal), "the live plan is a dry run");
        }

        var location776 = await Evaluate(http, Draft("location"), ForeSite, "776");
        Check.That(Outcome(location776) == "Runs" && Out(location776, "Latitude_out") is >= -90 and <= 90, "776 latitude is a valid coordinate");

        // CygNet demo: pump fillage exists on RPOC_0001 only.
        var fillage = new
        {
            name = "Fillage", inputs = new[] { new { name = "Fillage", sources = new[] { new { input = "PumpFillage", basis = (string?)null } }, required = true } },
            parameters = Array.Empty<object>(), steps = Array.Empty<object>(), outputs = new[] { new { name = "Fillage_out", kind = "value", expression = "Fillage", unit = "%" } }
        };
        var rpoc = await Evaluate(http, JsonSerializer.SerializeToElement(fillage, Web), CygNet, "RPOC_0001");
        Check.That(Outcome(rpoc) == "Runs" && Out(rpoc, "Fillage_out") is >= 0 and <= 100, "CygNet RPOC_0001 pump fillage is a percentage");
        var plain = await Evaluate(http, JsonSerializer.SerializeToElement(fillage, Web), CygNet, "8800_0001");
        Check.That(Outcome(plain) == "Skipped", "CygNet 8800_0001 has no pump fillage, so the module is skipped");

        Console.WriteLine("Live authoring checks passed (ForeSite 776 and 1596, CygNet RPOC_0001 and 8800_0001).");
    }

    private static string Outcome(JsonElement run) => run.GetProperty("outcome").GetString()!;

    private static double Out(JsonElement run, string name)
    {
        var value = run.GetProperty("outputs").EnumerateArray().Single(o => o.GetProperty("name").GetString() == name).GetProperty("value");
        Check.That(value.GetProperty("kind").GetString() == "Number", $"output {name} is a number, got {value}");
        return value.GetProperty("number").GetDouble();
    }

    private static Task<JsonElement> Evaluate(HttpClient http, JsonElement draft, Guid connection, string asset) =>
        Post(http, "authoring/evaluate", new { draft, connectionId = connection, externalId = asset });

    private static async Task<JsonElement> Get(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        Check.That(response.IsSuccessStatusCode, $"GET {path} returned {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> Post(HttpClient http, string path, object body)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body, Web), Encoding.UTF8, "application/json");
        // ForeSite intermittently resets a connection; one more attempt is what a user would do.
        for (var attempt = 1; ; attempt++)
        {
            using var response = await http.PostAsync(path, content);
            var text = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode) return JsonDocument.Parse(text).RootElement.Clone();
            if (attempt < 3 && response.StatusCode == HttpStatusCode.ServiceUnavailable) continue;
            throw new InvalidOperationException($"Authoring check failed: POST {path} returned {(int)response.StatusCode}: {text}");
        }
    }
}

/// <summary>ForeSite read directly as the current Windows identity, independent of the product's adapter.</summary>
internal static class ForeSiteDirect
{
    private static readonly HttpClientHandler Handler = new() { UseDefaultCredentials = true, CookieContainer = new CookieContainer() };
    private static readonly HttpClient Client = new(Handler)
    {
        BaseAddress = new Uri((Environment.GetEnvironmentVariable("FORESITE_BASE_URL") ?? "http://usdcfstappts010:80").TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(60)
    };
    private static bool _signedIn;

    public static async Task<JsonElement> GetAsync(string path)
    {
        if (!_signedIn)
        {
            using var token = await Client.GetAsync("TokenService/CreateToken");
            Check.That(token.IsSuccessStatusCode, $"ForeSite sign-in returned {(int)token.StatusCode}");
            _signedIn = true;
        }
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await Client.GetAsync(path);
                var text = await response.Content.ReadAsStringAsync();
                Check.That(response.IsSuccessStatusCode, $"ForeSite {path} returned {(int)response.StatusCode}");
                return JsonDocument.Parse(text).RootElement.Clone();
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(500 * attempt);
            }
        }
    }

    public static string Unit(JsonElement root, string field) =>
        root.TryGetProperty("Units", out var units) && units.TryGetProperty(field, out var entry) && entry.TryGetProperty("UnitKey", out var key)
            ? key.GetString() ?? "" : "";

    /// <summary>Finds a numeric field anywhere in the document; ForeSite nests surveillance values under "Value".</summary>
    public static double Number(JsonElement root, string field)
    {
        var scope = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Value", out var inner) && inner.ValueKind == JsonValueKind.Object ? inner : root;
        var found = Find(scope, field);
        Check.That(found is { ValueKind: JsonValueKind.Number }, $"ForeSite returned a numeric '{field}'");
        return found!.Value.GetDouble();
    }

    private static JsonElement? Find(JsonElement element, string field)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, field, StringComparison.OrdinalIgnoreCase)) return property.Value;
                if (Find(property.Value, field) is { } nested) return nested;
            }
        return null;
    }
}
