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
    private const string GroupAssetTypeKey = "foresite-group";
    private const string GroupSchema =
        """{"type":"object","properties":{"level":{"type":"string"}},"additionalProperties":true}""";

    // Template levels read the discovered metadata of the same name; "well" is always the leaf.
    private static readonly Dictionary<string, string> LevelFallbacks = new()
    {
        ["asset"] = "Unassigned", ["wellType"] = "Unknown type", ["deviceType"] = "Unspecified device", ["well"] = "",
        ["site"] = "Unknown site", ["service"] = "Unknown service", ["facilityType"] = "Unknown facility type"
    };

    private static readonly HierarchyTemplateResponse[] Templates =
    [
        new("asset-type-device-well", "Asset > Well type > Device type > Well", ["asset", "wellType", "deviceType", "well"]),
        new("asset-type-well", "Asset > Well type > Well", ["asset", "wellType", "well"]),
        new("cygnet-site-service-facility", "CygNet: Site > Service > Facility", ["site", "service", "well"])
    ];

    private const string WellSchema =
        """{"type":"object","required":["externalId","connectionId"],"properties":{"externalId":{"type":"string"},"connectionId":{"type":"string"}},"additionalProperties":true}""";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void AddConnectionClient(this IServiceCollection services, IConfiguration configuration) =>
        services.AddHttpClient(GatewayClient, client =>
        {
            client.BaseAddress = new Uri((configuration["Runtime:IntegrationGatewayUrl"] ?? "http://127.0.0.1:5081")
                .TrimEnd('/') + "/api/internal/v1/");
            client.Timeout = TimeSpan.FromSeconds(60);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseDefaultCredentials = true });

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

        admin.MapGet("/templates", () => Results.Ok(Templates))
            .WithName("ListHierarchyTemplates").Produces<HierarchyTemplateResponse[]>();

        admin.MapPost("/{id:guid}/discover/apply", async (Guid id, ApplyDiscoveryRequest request, IHttpClientFactory f,
            AssetService assets, RequestAccessContext access, HttpContext http, CancellationToken ct) =>
        {
            var levels = request.Levels is { Length: > 0 }
                ? request.Levels
                : Templates.FirstOrDefault(t => t.Key == request.TemplateKey)?.Levels;
            if (levels is null || levels[^1] != "well" || levels.Distinct().Count() != levels.Length ||
                levels.Any(l => !LevelFallbacks.ContainsKey(l)))
                throw new WellSiteAutoPilotException("CONNECTION_TEMPLATE_INVALID", FailureKind.Validation,
                    "A template needs distinct, known levels (asset, wellType, deviceType, site, service, facilityType) and must end with well.");
            var (result, discovery) = await Discover(f, assets, id, ct);
            if (result is not null || discovery is null) return result!;
            var types = await assets.ListAssetTypesAsync(ct);
            var actor = access.CreateActor(http);
            var wellType = types.FirstOrDefault(t => t.Key == WellAssetTypeKey) ??
                await assets.CreateAssetTypeAuthorizedAsync(
                    new CreateAssetTypeCommand(WellAssetTypeKey, "ForeSite well", WellSchema), access.Subject, actor, ct);
            var groupType = types.FirstOrDefault(t => t.Key == GroupAssetTypeKey) ??
                await assets.CreateAssetTypeAuthorizedAsync(
                    new CreateAssetTypeCommand(GroupAssetTypeKey, "ForeSite group", GroupSchema), access.Subject, actor, ct);

            var groups = (await assets.ListAssetsAsync(groupType.Id, null, 500, ct)).ToList();
            async Task<Guid> Group(Guid? parent, string level, string name)
            {
                var existing = groups.FirstOrDefault(g => g.ParentAssetId == parent && g.Name == name);
                if (existing is not null) return existing.Id;
                var attrs = JsonSerializer.Serialize(new Dictionary<string, string> { ["level"] = level }, Json);
                var created = await CreateWithFallback(assets, groupType.Id, name, parent, attrs, name + " (" + level + ")", access, actor, ct);
                groups.Add(created);
                return created.Id;
            }

            var createdCount = 0;
            foreach (var asset in discovery.Assets.Where(a => !a.AlreadyImported))
            {
                Guid? parent = null;
                foreach (var level in levels[..^1])
                    parent = await Group(parent, level, asset.Metadata.GetValueOrDefault(level) is { Length: > 0 } v ? v : LevelFallbacks[level]);
                var values = new Dictionary<string, string>(asset.Metadata)
                {
                    ["externalId"] = asset.ExternalId,
                    ["connectionId"] = id.ToString()
                };
                await CreateWithFallback(assets, wellType.Id, asset.DisplayName, parent,
                    JsonSerializer.Serialize(values, Json), $"{asset.DisplayName} [{asset.ExternalId}]", access, actor, ct);
                createdCount++;
            }
            return Results.Ok(new ApplyDiscoveryResponse(createdCount, discovery.Assets.Length - createdCount, wellType.Id));
        }).WithName("ApplyConnectionDiscovery").NotifiesChange(ChangeArea.Assets).Produces<ApplyDiscoveryResponse>();
    }

    // Names are not unique in ForeSite (or across hierarchy branches); retry once with a disambiguated name.
    private static async Task<Asset> CreateWithFallback(AssetService assets, Guid typeId, string name, Guid? parent,
        string json, string fallbackName, RequestAccessContext access, SecurityActorContext actor, CancellationToken ct)
    {
        try
        {
            return await assets.CreateAssetAuthorizedAsync(new CreateAssetCommand(typeId, name, parent, json), access.Subject, actor, ct);
        }
        catch (WellSiteAutoPilotException ex) when (ex.Kind == FailureKind.Conflict)
        {
            return await assets.CreateAssetAuthorizedAsync(new CreateAssetCommand(typeId, fallbackName, parent, json), access.Subject, actor, ct);
        }
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
