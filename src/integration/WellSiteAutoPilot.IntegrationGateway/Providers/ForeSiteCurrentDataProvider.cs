using System.Globalization;
using System.Net;
using System.Text.Json;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.IntegrationGateway.Providers;

/// <summary>POC: reads current ForeSite surveillance values for one well. The client keeps a cookie
/// container so the TokenService session survives across reads; a 401 re-creates the token.</summary>
public sealed class ForeSiteCurrentDataProvider(HttpClient httpClient) : ICurrentDataProvider, IDisposable
{
    private static readonly Dictionary<string, (string Field, string Unit)> Quantities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["PumpFillage"] = ("CurrentPumpFillage", "%"),
            ["AveragePumpFillage"] = ("AveragePumpFillage", "%")
        };

    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private bool _hasToken;

    public async ValueTask<EngineeringValue> GetCurrentAsync(
        string assetExternalId, string quantity, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetExternalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(quantity);
        if (!Quantities.TryGetValue(quantity, out var map))
            throw new ArgumentException($"ForeSite quantity '{quantity}' is not supported.", nameof(quantity));
        if (!long.TryParse(assetExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out var wellId))
            throw new ArgumentException("ForeSite well id must be numeric.", nameof(assetExternalId));

        var path = $"Surveillance/well/{wellId.ToString(CultureInfo.InvariantCulture)}";
        using var response = await SendAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement.GetProperty("Value");
        var value = root.GetProperty(map.Field);
        if (value.ValueKind != JsonValueKind.Number)
            throw new InvalidOperationException($"ForeSite returned no {quantity} value.");
        var scan = root.GetProperty("LastScanTime").GetDateTimeOffset().ToUniversalTime();
        var good = root.TryGetProperty("LastGoodScanTime", out var goodScan) &&
                   goodScan.ValueKind == JsonValueKind.String &&
                   goodScan.GetDateTimeOffset().ToUniversalTime() == scan;
        return new EngineeringValue(quantity, value.GetDecimal(), map.Unit, scan, good ? "Good" : "Uncertain", "foresite");
    }

    public void Dispose() => _tokenGate.Dispose();

    private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken ct)
    {
        await EnsureTokenAsync(false, ct);
        var response = await httpClient.GetAsync(path, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
        response.Dispose();
        await EnsureTokenAsync(true, ct);
        return await httpClient.GetAsync(path, ct);
    }

    private async Task EnsureTokenAsync(bool force, CancellationToken ct)
    {
        await _tokenGate.WaitAsync(ct);
        try
        {
            if (_hasToken && !force) return;
            using var response = await httpClient.GetAsync("TokenService/CreateToken", ct);
            response.EnsureSuccessStatusCode();
            _hasToken = true;
        }
        finally { _tokenGate.Release(); }
    }
}
