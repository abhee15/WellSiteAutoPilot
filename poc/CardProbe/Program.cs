using WellSiteAutoPilot.Integration.ForeSite;

// Usage: CardProbe <baseUrl> <wellId>. Read-only; Windows integrated auth.
var handler = new HttpClientHandler { UseDefaultCredentials = true, UseCookies = false };
using var client = new HttpClient(handler) { BaseAddress = ForeSiteAdapter.BaseAddress(args[0]) };
var session = new ForeSiteSession(client);
var value = await ForeSiteCards.ReadAsync(session, args[1], "SurfaceCards", CancellationToken.None);
Console.WriteLine($"quality={value.Quality} source={value.Source} timestamp={value.TimestampUtc:O} bytes={value.StructuredJson!.Length}");
using var doc = System.Text.Json.JsonDocument.Parse(value.StructuredJson);
foreach (var card in doc.RootElement.EnumerateArray())
    Console.WriteLine($"{card.GetProperty("kind")} {card.GetProperty("capturedAtUtc")} pts={card.GetProperty("position").GetArrayLength()} " +
        $"units={card.GetProperty("positionUnit")}/{card.GetProperty("loadUnit")}");
