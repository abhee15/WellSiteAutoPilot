using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WellSiteAutoPilot.Http;
using WellSiteAutoPilot.Http.ServiceCallers;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet;
using WellSiteAutoPilot.Integration.CygNet.Adapter;
using WellSiteAutoPilot.IntegrationGateway.Adapters;

internal static class PointCatalogChecks
{
    public static async Task RunAsync()
    {
        const string serverKey = "cygnet-catalog-server";
        const string workerKey = "cygnet-catalog-worker";
        using var simulator = Environment.GetEnvironmentVariable("WSA_CAS_SIMULATOR_ROOT") is { } url
            ? new HttpClient { BaseAddress = new Uri(url) } : null;
        var source = new PointClient(simulator);
        var catalog = new AdapterCatalog(source);
        var operation = CygNetPointCatalog.Operation;
        Check(catalog.Find("cygnet")!.ReadCapabilityGroups().Single(g => g.Key == "point-catalog").OperationKeys.Single() == operation.Key &&
            new AdapterCatalog(new CygNetUnconfiguredClient()).Find("cygnet")!.ReadOperations().All(o => o.Key != operation.Key) &&
            new AdapterCatalog(new CygNetApiClient()).Find("cygnet")!.ReadOperations().Any(o => o.Key == operation.Key),
            "Catalog descriptor must follow backend support.");

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["Security:Callers:server:Verifier"] = ServiceCallerKeys.VerifierFor(serverKey);
        builder.Configuration["Security:Callers:worker:Verifier"] = ServiceCallerKeys.VerifierFor(workerKey);
        builder.Services.AddServiceCallerAuthentication(builder.Configuration);
        builder.Services.AddWellSiteHttpErrorHandling();
        builder.Services.AddSingleton(catalog);
        await using var app = builder.Build();
        app.UseWellSiteHttpErrorHandling();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGroup("/api/internal/v1").MapRegisteredAdapterReads();
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };

        var request = new AdapterReadInvocation<CygNetPointCatalogRequest>(Guid.NewGuid(), 2,
            new Dictionary<string, string?> { ["domain"] = "1", ["siteService"] = "SITE.UIS", ["facilityFilter"] = "WELL_*" },
            CygNetAdapter.ResourceKind, "SITE.UIS::WELL_OK", "1.0", new("SITE.UIS::WELL_OK"));
        await Send(request, HttpStatusCode.Unauthorized, caller: null);
        await Send(request, HttpStatusCode.Forbidden, caller: "worker:" + workerKey);
        await Send(request, HttpStatusCode.NotFound, adapter: "foresite");
        await Send(request with { ContractVersion = "2.0" }, HttpStatusCode.Conflict);
        await Send(request with { Input = new("SITE.UIS::WELL_OTHER") }, HttpStatusCode.BadRequest);
        await Send(For("OTHER.UIS::WELL_OK"), HttpStatusCode.BadRequest);
        await Send(For("SITE.UIS::TANK_01"), HttpStatusCode.BadRequest);
        await Send(For("SITE.UIS::WELL_*"), HttpStatusCode.BadRequest);
        Check(source.Calls == 0, "Unauthorized or out-of-scope input reached PNT.");

        var result = await Send(request, HttpStatusCode.OK);
        Check(result is { AssetExternalId: "SITE.UIS::WELL_OK", Truncated: false } &&
            result.Points.SequenceEqual(new CygNetPointCatalogEntry[]
            {
                new("cygnet.udc.PRCASXIN", "prcasxin", "Casing Pressure", "psi"),
                new("cygnet.udc.RRLSTKYEST", "RRLSTKYEST", null, "")
            }), "Metadata, original UDC and units, or deterministic ordering changed.");
        Check(await Send(For("SITE.UIS::WELL_EMPTY"), HttpStatusCode.OK) is { Points.Count: 0, Truncated: false },
            "Empty facility data must stay empty.");
        Check(await Send(For("SITE.UIS::WELL_TRUNCATED"), HttpStatusCode.OK) is { Points.Count: CygNetPointCatalog.MaximumPoints, Truncated: true },
            "Truncation evidence was lost.");

        foreach (var well in new[] { "WELL_MISMATCH", "WELL_WRONG_SERVICE", "WELL_DUPLICATE", "WELL_OVERSIZED", "WELL_BAD_CODE" })
            await Send(For("SITE.UIS::" + well), HttpStatusCode.ServiceUnavailable, error: "CYGNET_CATALOG_RESPONSE_INVALID");
        Check(!AdapterReadRules.Find("cygnet")!.AllowsCurrentRead("cygnet.udc.PRCASXIN", null, null),
            "Catalog presence must not enable arbitrary current reads.");
        await CheckAlarmsAsync(http, request, serverKey, workerKey, source);
        Console.WriteLine(simulator is null
            ? "CygNet CAS alarms: Gateway checks passed with the offline test backend."
            : "CygNet CAS alarms: Gateway checks passed with the HTTP Provider Simulator.");
        Console.WriteLine("CygNet point catalog: Gateway authorization, facility scope, metadata, truncation and invalid provider responses passed (test backend).");

        AdapterReadInvocation<CygNetPointCatalogRequest> For(string asset) => request with { ExternalId = asset, Input = new(asset) };

        async Task<CygNetPointCatalogResult?> Send(AdapterReadInvocation<CygNetPointCatalogRequest> body, HttpStatusCode expected,
            string? caller = "server:" + serverKey, string adapter = "cygnet", string? error = null)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/v1/adapters/{adapter}/reads/{operation.Key}")
            {
                Content = JsonContent.Create(body)
            };
            if (caller is not null) message.Headers.Authorization = new AuthenticationHeaderValue(ServiceCaller.Scheme, caller);
            using var response = await http.SendAsync(message);
            Check(response.StatusCode == expected, $"Point catalog expected {expected}, got {response.StatusCode}.");
            if (error is not null)
                Check((await response.Content.ReadFromJsonAsync<WellSiteProblemDetails>())?.Code == error, "Wrong provider failure code.");
            return expected == HttpStatusCode.OK ? await response.Content.ReadFromJsonAsync<CygNetPointCatalogResult>() : null;
        }
    }

    private static async Task CheckAlarmsAsync(HttpClient http, AdapterReadInvocation<CygNetPointCatalogRequest> catalogRequest,
        string serverKey, string workerKey, PointClient source)
    {
        var operation = CygNetActiveAlarms.Operation;
        var expected = "SITE.UIS::WELL_ALARM";
        Check(CygNetActiveAlarms.MatchesScopedPointTag(expected, "RRLPFILCUR", "SITE.UIS::WELL_ALARM.RRLPFILCUR") &&
            CygNetActiveAlarms.MatchesScopedPointTag(expected, "RRLPFILCUR", "site.uis.000123:LONG_TAG:WELL_ALARM.RRLPFILCUR") &&
            !CygNetActiveAlarms.MatchesScopedPointTag(expected, "RRLPFILCUR", "SITE.UIS.000123:LONG_TAG:OTHER.RRLPFILCUR") &&
            !CygNetActiveAlarms.MatchesScopedPointTag(expected, "RRLPFILCUR", "OTHER.UIS::WELL_ALARM.RRLPFILCUR") &&
            !CygNetActiveAlarms.MatchesScopedPointTag(expected, "RRLPFILCUR", "RRLPFILCUR"), "Native CAS identity scope changed.");
        var request = new AdapterReadInvocation<CygNetActiveAlarmRequest>(catalogRequest.ConnectionId, catalogRequest.ConnectionRevision,
            catalogRequest.Settings, catalogRequest.ResourceKind, expected, operation.ContractVersion, new(expected, "RRLPFILCUR"));

        await Send(request, HttpStatusCode.Unauthorized, caller: null);
        await Send(request, HttpStatusCode.Forbidden, caller: "worker:" + workerKey);
        await Send(request, HttpStatusCode.NotFound, adapter: "foresite");
        await Send(request with { ContractVersion = "2.0" }, HttpStatusCode.Conflict);
        await Send(request with { Input = new("SITE.UIS::OTHER", request.Input.Udc) }, HttpStatusCode.BadRequest);
        await Send(request with { Input = request.Input with { Udc = "*" } }, HttpStatusCode.BadRequest);
        await Send(For("OTHER.UIS::WELL_ALARM"), HttpStatusCode.BadRequest);
        Check(source.AlarmCalls == 0, "Rejected alarm calls reached the provider.");

        var active = await Send(request, HttpStatusCode.OK);
        Check(active is { Alarms.Count: 2, Truncated: false } && active.AssetExternalId == expected && active.Udc == "RRLPFILCUR" &&
            active.RetrievedAtUtc.Year > 2023 && active.Alarms[0] == new CygNetActiveAlarm(2, "2023-01-01T12:00:00.0000000", "Unspecified") &&
            active.Alarms[1] == new CygNetActiveAlarm(3, null, null), "CAS state or source clock evidence changed.");
        Check(await Send(For("SITE.UIS::WELL_ALARM_EMPTY"), HttpStatusCode.OK) is { Alarms.Count: 0, Truncated: false },
            "Cleared alarms appeared in the active list.");
        Check(await Send(For("SITE.UIS::WELL_ALARM_TRUNCATED"), HttpStatusCode.OK) is { Alarms.Count: CygNetActiveAlarms.MaximumRecords, Truncated: true },
            "CAS truncation was lost.");
        await Send(For("SITE.UIS::WELL_ALARM_MISMATCH"), HttpStatusCode.ServiceUnavailable, errorCode: "CYGNET_ALARM_RESPONSE_INVALID");
        await Send(For("SITE.UIS::WELL_ALARM_MISSING"), HttpStatusCode.ServiceUnavailable, errorCode: "CYGNET_CAS_READ_FAILED");

        AdapterReadInvocation<CygNetActiveAlarmRequest> For(string tag) =>
            request with { ExternalId = tag, Input = request.Input with { AssetExternalId = tag } };

        async Task<CygNetActiveAlarmResult?> Send(AdapterReadInvocation<CygNetActiveAlarmRequest> body, HttpStatusCode status,
            string? caller = null, string adapter = "cygnet", string? errorCode = null)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/v1/adapters/{adapter}/reads/{operation.Key}")
            {
                Content = JsonContent.Create(body)
            };
            if (status != HttpStatusCode.Unauthorized)
                message.Headers.Authorization = new AuthenticationHeaderValue(ServiceCaller.Scheme, caller ?? "server:" + serverKey);
            using var response = await http.SendAsync(message);
            Check(response.StatusCode == status, $"CAS expected {status}, got {response.StatusCode}.");
            if (errorCode is not null)
                Check((await response.Content.ReadFromJsonAsync<WellSiteProblemDetails>())?.Code == errorCode, "Wrong CAS failure code.");
            return status == HttpStatusCode.OK ? await response.Content.ReadFromJsonAsync<CygNetActiveAlarmResult>() : null;
        }
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }

    private sealed class PointClient(HttpClient? simulator) : ICygNetClient, ICygNetPointCatalogClient, ICygNetAlarmClient
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);

        private int alarmCalls;
        public int AlarmCalls => Volatile.Read(ref alarmCalls);

        public async Task<CygNetPointAlarmBatch> ReadPointAlarmsAsync(CygNetConnection connection, string facilityId, string udc,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref alarmCalls);
            Check(connection is { Domain: 1, SiteService: "SITE.UIS", FacilityFilter: "WELL_*" } && udc == "RRLPFILCUR",
                "CAS backend received the wrong connection or point.");
            if (simulator is not null)
            {
                using var response = await simulator.GetAsync($"cygnet/fixtures/facilities/{facilityId}/points/{udc}/alarms", cancellationToken);
                if (!response.IsSuccessStatusCode) throw Missing();
                return await response.Content.ReadFromJsonAsync<CygNetPointAlarmBatch>(cancellationToken) ?? throw Missing();
            }

            var set = new CygNetPointAlarm(true, 2, new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Unspecified));
            var cleared = new CygNetPointAlarm(false, 1, new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));
            var tag = connection.TagFor(facilityId);
            return facilityId switch
            {
                "WELL_ALARM" => new(tag, udc, [set, cleared, new(true, 3, default)], false),
                "WELL_ALARM_EMPTY" => new(tag, udc, [cleared], false),
                "WELL_ALARM_TRUNCATED" => new(tag, udc, Enumerable.Repeat(set, CygNetActiveAlarms.MaximumRecords).ToArray(), true),
                "WELL_ALARM_MISMATCH" => new(connection.TagFor("OTHER"), udc, [set], false),
                _ => throw Missing()
            };
        }

        private static CygNetReadException Missing() => new("CYGNET_CAS_READ_FAILED", "Synthetic CAS unavailable.");

        public Task<CygNetPointList> ListPointsAsync(CygNetConnection connection, string facilityId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            Check(connection is { Domain: 1, SiteService: "SITE.UIS", FacilityFilter: "WELL_*" }, "Point backend received the wrong connection.");
            var tag = connection.TagFor(facilityId);
            return Task.FromResult(facilityId switch
            {
                "WELL_OK" => new CygNetPointList(tag, [new("RRLSTKYEST", null, ""), new("prcasxin", "Casing Pressure", "psi")], false),
                "WELL_EMPTY" => new(tag, [], false),
                "WELL_TRUNCATED" => new(tag, Points(CygNetPointCatalog.MaximumPoints), true),
                "WELL_OVERSIZED" => new(tag, Points(CygNetPointCatalog.MaximumPoints + 1), false),
                "WELL_MISMATCH" => new(connection.TagFor("OTHER_FACILITY"), [new("Point", null, null)], false),
                "WELL_WRONG_SERVICE" => new("OTHER.UIS::" + facilityId, [new("Point", null, null)], false),
                "WELL_DUPLICATE" => new(tag, [new("Point", null, null), new("POINT", null, null)], false),
                "WELL_BAD_CODE" => new(tag, [new("point.*", null, null)], false),
                _ => throw new InvalidOperationException("Unexpected facility.")
            });
        }

        private static CygNetPointMetadata[] Points(int count) => Enumerable.Range(1, count)
            .Select(index => new CygNetPointMetadata("POINT_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), new string('日', 96), new string('日', 32))).ToArray();

        public Task<IReadOnlyList<string>> CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EngineeringValue> ReadCurrentAsync(CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
