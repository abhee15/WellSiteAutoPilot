using System.Net;
using System.Text.Json;
using WellSiteAutoPilot.Api.Contracts.Connections;
using WellSiteAutoPilot.Application.Assets;
using WellSiteAutoPilot.Application.Changes;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Assets;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Server.Changes;
using WellSiteAutoPilot.Server.Security;

namespace WellSiteAutoPilot.Server.Connections;

/// <summary>
/// POC Administration endpoints for connections. The Gateway owns connection records and adapters;
/// the Server enforces admin-only access and owns the Asset import.
/// </summary>
public static class ConnectionEndpoints
{
    private const string GatewayClient = "gateway-connections";
    private const string WellAssetTypeKey = "foresite-well";
    private const string WellSchema =
        """{"type":"object","required":["externalId","connectionId"],"properties":{"externalId":{"type":"string"},"connectionId":{"type":"string"}},"additionalProperties":true}""";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void AddConnectionClient(this IServiceCollection services, IConfiguration configuration) =>
        services.AddHttpClient(GatewayClient, client =>
        {
            client.BaseAddress = new Uri((configuration["Runtime:IntegrationGatewayUrl"] ?? "http://127.0.0.1:5081")
                .TrimEnd('/') + "/api/internal/v1/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

    public static void MapConnectionEndpoints(this RouteGroupBuilder v1)
    {
        var admin = v1.MapGroup("/connections").RequireAuthorization(WellSitePolicies.SecurityManage);

        admin.MapGet("/adapters", async (IHttpClientFactory f, CancellationToken ct) =>
            Results.Ok(await Read<AdapterResponse[]>(f, HttpMethod.Get, "adapters", null, ct) ?? []))
            .WithName("ListConnectionAdapters").Produces<AdapterResponse[]>();

        admin.MapGet("", async (IHttpClientFactory f, CancellationToken ct) =>
            Results.Ok(await Read<ConnectionResponse[]>(f, HttpMethod.Get, "connections", null, ct) ?? []))
            .WithName("ListConnections").Produces<ConnectionResponse[]>();

        admin.MapPost("", (ConnectionRequest r, IHttpClientFactory f, CancellationToken ct) =>
            Forward<ConnectionResponse>(f, HttpMethod.Post, "connections", r, ct))
            .WithName("CreateConnection").Produces<ConnectionResponse>(StatusCodes.Status201Created);

        admin.MapPut("/{id:guid}", (Guid id, ConnectionRequest r, IHttpClientFactory f, CancellationToken ct) =>
            Forward<ConnectionResponse>(f, HttpMethod.Put, $"connections/{id}", r, ct))
            .WithName("UpdateConnection").Produces<ConnectionResponse>();

        admin.MapDelete("/{id:guid}", (Guid id, IHttpClientFactory f, CancellationToken ct) =>
            Forward<object>(f, HttpMethod.Delete, $"connections/{id}", null, ct))
            .WithName("DeleteConnection").Produces(StatusCodes.Status204NoContent);

        admin.MapPost("/{id:guid}/test", (Guid id, IHttpClientFactory f, CancellationToken ct) =>
            Forward<ConnectionResponse>(f, HttpMethod.Post, $"connections/{id}/test", null, ct))
            .WithName("TestConnection").Produces<ConnectionResponse>();

        admin.MapPost("/{id:guid}/discover", async (Guid id, IHttpClientFactory f, AssetService assets,
            CancellationToken ct) =>
        {
            var (result, discovery) = await Discover(f, assets, id, ct);
            return result ?? Results.Ok(discovery);
        }).WithName("DiscoverConnectionAssets").Produces<DiscoveryResponse>();

        admin.MapPost("/{id:guid}/discover/apply", async (Guid id, IHttpClientFactory f, AssetService assets,
            RequestAccessContext access, HttpContext http, CancellationToken ct) =>
        {
            var (result, discovery) = await Discover(f, assets, id, ct);
            if (result is not null || discovery is null) return result!;
            var types = await assets.ListAssetTypesAsync(ct);
            var type = types.FirstOrDefault(t => t.Key == WellAssetTypeKey) ??
                await assets.CreateAssetTypeAuthorizedAsync(
                    new CreateAssetTypeCommand(WellAssetTypeKey, "ForeSite well", WellSchema),
                    access.Subject, access.CreateActor(http), ct);
            var created = 0;
            foreach (var asset in discovery.Assets.Where(a => !a.AlreadyImported))
            {
                var values = new Dictionary<string, string>(asset.Metadata)
                {
                    ["externalId"] = asset.ExternalId,
                    ["connectionId"] = id.ToString()
                };
                var json = JsonSerializer.Serialize(values, Json);
                try
                {
                    await assets.CreateAssetAuthorizedAsync(
                        new CreateAssetCommand(type.Id, asset.DisplayName, null, json),
                        access.Subject, access.CreateActor(http), ct);
                }
                catch (WellSiteAutoPilotException ex) when (ex.Kind == FailureKind.Conflict)
                {
                    // Display names are not unique in ForeSite; the external id disambiguates.
                    await assets.CreateAssetAuthorizedAsync(
                        new CreateAssetCommand(type.Id, $"{asset.DisplayName} [{asset.ExternalId}]", null, json),
                        access.Subject, access.CreateActor(http), ct);
                }
                created++;
            }
            return Results.Ok(new ApplyDiscoveryResponse(created, discovery.Assets.Length - created, type.Id));
        }).WithName("ApplyConnectionDiscovery").NotifiesChange(ChangeArea.Assets).Produces<ApplyDiscoveryResponse>();
    }

    // Discovery is a proposal: mark what is already imported so Apply stays idempotent per connection + external id.
    private static async Task<(IResult? Error, DiscoveryResponse? Discovery)> Discover(
        IHttpClientFactory f, AssetService assets, Guid id, CancellationToken ct)
    {
        using var response = await Send(f, HttpMethod.Post, $"connections/{id}/discover", null, ct);
        if (!response.IsSuccessStatusCode) return (await Translate(response, ct), null);
        var raw = await response.Content.ReadFromJsonAsync<DiscoveryResponse>(Json, ct)
            ?? throw new WellSiteAutoPilotException("CONNECTION_GATEWAY_INVALID", FailureKind.DependencyUnavailable,
                "The gateway returned an empty discovery result.");
        var imported = new HashSet<string>(StringComparer.Ordinal);
        var type = (await assets.ListAssetTypesAsync(ct)).FirstOrDefault(t => t.Key == WellAssetTypeKey);
        if (type is not null)
            foreach (var existing in await assets.ListAssetsAsync(type.Id, null, 500, ct))
            {
                using var doc = JsonDocument.Parse(existing.AttributeValuesJson);
                if (doc.RootElement.TryGetProperty("connectionId", out var c) && c.GetString() == id.ToString() &&
                    doc.RootElement.TryGetProperty("externalId", out var e) && e.GetString() is { } ext)
                    imported.Add(ext);
            }
        return (null, raw with
        {
            Assets = raw.Assets.Select(a => a with { AlreadyImported = imported.Contains(a.ExternalId) }).ToArray()
        });
    }

    private static Task<HttpResponseMessage> Send(
        IHttpClientFactory f, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        return SendRequest(f, request, ct);
    }

    private static async Task<HttpResponseMessage> SendRequest(
        IHttpClientFactory f, HttpRequestMessage request, CancellationToken ct)
    {
        try { return await f.CreateClient(GatewayClient).SendAsync(request, ct); }
        catch (HttpRequestException ex)
        {
            throw new WellSiteAutoPilotException("INTEGRATION_GATEWAY_UNAVAILABLE", FailureKind.DependencyUnavailable,
                "The integration gateway is not reachable.", innerException: ex);
        }
    }

    private static async Task<T?> Read<T>(
        IHttpClientFactory f, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await Send(f, method, path, body, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private static async Task<IResult> Forward<T>(
        IHttpClientFactory f, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await Send(f, method, path, body, ct);
        if (!response.IsSuccessStatusCode) return await Translate(response, ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return Results.NoContent();
        var payload = await response.Content.ReadFromJsonAsync<T>(Json, ct);
        return response.StatusCode == HttpStatusCode.Created ? Results.Created("/api/v1/connections", payload) : Results.Ok(payload);
    }

    private static async Task<IResult> Translate(HttpResponseMessage response, CancellationToken ct)
    {
        string code = "CONNECTION_FAILED", message = "The connection operation failed.";
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("error", out var e)) code = e.GetString() ?? code;
            if (doc.RootElement.TryGetProperty("message", out var m)) message = m.GetString() ?? message;
        }
        catch (JsonException) { }
        var kind = response.StatusCode switch
        {
            HttpStatusCode.NotFound => FailureKind.NotFound,
            HttpStatusCode.Conflict => FailureKind.Conflict,
            HttpStatusCode.BadRequest => FailureKind.Validation,
            _ => FailureKind.DependencyUnavailable
        };
        throw new WellSiteAutoPilotException("CONNECTION_" + code, kind, message);
    }
}
