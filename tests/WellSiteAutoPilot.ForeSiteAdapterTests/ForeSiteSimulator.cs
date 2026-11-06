using System.Net;
using System.Text;

/// <summary>
/// Offline ForeSite for tests and CI: serves responses recorded read-only from a live ForeSite (well 776, WellName sanitized).
/// Only well 776 exists; every other well returns 404. All routes are GET, and the token handshake sets a cookie.
/// </summary>
internal sealed class ForeSiteSimulator : HttpMessageHandler
{
    public const string WellId = "776";
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add(request.Method + " " + path);
        if (path.EndsWith("TokenService/CreateToken", StringComparison.Ordinal))
        {
            var token = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            token.Headers.TryAddWithoutValidation("Set-Cookie", "fs=sim; Path=/");
            return Task.FromResult(token);
        }
        if (request.Method != HttpMethod.Get) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
        var fixture = path switch
        {
            _ when path.EndsWith("/Surveillance/well/" + WellId, StringComparison.Ordinal) => "status.json",
            _ when path.EndsWith("/Surveillance/Well/AllSavedSetPointParams", StringComparison.Ordinal) &&
                request.RequestUri.Query.Contains("WellId=" + WellId, StringComparison.Ordinal) => "setpoints.json",
            _ when path.EndsWith("/Alarms/CurrentAlarms/" + WellId, StringComparison.Ordinal) => "alarms.json",
            _ when path.Contains("/Surveillance/Well/DailyAverage/" + WellId + "/", StringComparison.Ordinal) => "dailyaverage.json",
            _ => null
        };
        if (fixture is null) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(File.ReadAllText(Path.Combine(Directory, fixture)), Encoding.UTF8, "application/json")
        });
    }
}
