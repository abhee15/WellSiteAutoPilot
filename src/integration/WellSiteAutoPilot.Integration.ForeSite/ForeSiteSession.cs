using System.Net;

namespace WellSiteAutoPilot.Integration.ForeSite;

// A session belongs to one saved connection/revision. Callers serialize access;
// the same cookie and one-401 retry implementation serves discovery/test/reads.
/// <summary>Cookie session for one saved connection/revision or one discovery operation.</summary>
public sealed class ForeSiteSession(HttpClient client)
{
    private CookieContainer _cookies = new();
    private bool _initialized;

    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct) =>
        SendAuthenticatedAsync(HttpMethod.Get, path, null, ct);

    public Task<HttpResponseMessage> PostAsync(string path, object filter, CancellationToken ct) =>
        SendAuthenticatedAsync(HttpMethod.Post, path, ForeSiteAdapter.SerializeFilter(filter), ct);

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(HttpMethod method, string path, string? body, CancellationToken ct)
    {
        if (!_initialized) await CreateTokenAsync(ct);
        var response = await SendAsync(method, path, body, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
        response.Dispose();
        _cookies = new CookieContainer();
        _initialized = false;
        await CreateTokenAsync(ct);
        return await SendAsync(method, path, body, ct);
    }

    private async Task CreateTokenAsync(CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, ForeSiteAdapter.TokenPath, null, ct);
        if (!response.IsSuccessStatusCode)
            throw new ForeSiteTokenException(await ForeSiteAdapter.ExplainAsync(response, "the Gateway Windows account", ct));
        _initialized = true;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        // Each attempt owns fresh content, so a 401 retry can safely replay the exact reviewed filter.
        if (body is not null) request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var uri = new Uri(client.BaseAddress ?? throw new InvalidOperationException("ForeSite requires a base address."), path);
        var cookies = _cookies.GetCookieHeader(uri);
        if (!string.IsNullOrEmpty(cookies)) request.Headers.TryAddWithoutValidation("Cookie", cookies);
        var response = await client.SendAsync(request, ct);
        if (response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            try
            {
                foreach (var header in headers) _cookies.SetCookies(uri, header);
            }
            catch (CookieException)
            {
                response.Dispose();
                throw new ForeSiteTokenException("ForeSite returned an invalid session cookie.");
            }
        }
        return response;
    }
}

public sealed class ForeSiteTokenException(string message) : Exception(message) { }

