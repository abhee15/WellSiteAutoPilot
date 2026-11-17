using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.ProviderSimulator.Simulation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "WellSite AutoPilot Provider Simulator",
        Version = "v1",
        Description = "Development and test provider simulation endpoints."
    });
});
builder.Services.AddSingleton<SimulatorState>();
builder.Services.AddSingleton<SimulatorCommandBoard>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.MapGet("/api/sim/v1/state", (SimulatorState state) =>
    Results.Ok(new
    {
        mode = "SIMULATION",
        scenario = state.Scenario,
        providerAvailable = state.ProviderAvailable
    }))
    .WithName("GetSimulatorState");

app.MapPost("/api/sim/v1/scenarios/reset", (ResetScenarioRequest request, SimulatorState state, SimulatorCommandBoard commands) =>
    {
        state.Reset(request.Scenario);
        commands.Reset();
        return Results.NoContent();
    })
    .WithName("ResetSimulatorScenario");

app.MapPost("/api/sim/v1/provider/availability", (SetAvailabilityRequest request, SimulatorState state) =>
    {
        state.SetAvailability(request.Available);
        return Results.NoContent();
    })
    .WithName("SetSimulatorProviderAvailability");

app.MapPut("/api/sim/v1/assets/{assetId}/current/{quantity}",
    (string assetId, string quantity, SetCurrentValueRequest request, SimulatorState state) =>
    {
        state.SetCurrent(assetId, quantity, request.Value, request.Unit, request.Quality);
        return Results.NoContent();
    })
    .WithName("SetSimulatedCurrentValue");

app.MapGet("/api/sim/v1/assets/{assetId}/current/{quantity}",
    (string assetId, string quantity, SimulatorState state) =>
    {
        try
        {
            return Results.Ok(state.GetCurrent(assetId, quantity));
        }
        catch (SimulatorProviderUnavailableException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("GetSimulatedCurrentValue")
    .Produces<EngineeringValue>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status503ServiceUnavailable);

// Synthetic managed-CAS records for the Gateway adapter's offline integration checks.
app.MapGet("/cygnet/fixtures/facilities/{facilityId}/points/{udc}/alarms",
    (string facilityId, string udc, SimulatorState state) =>
    {
        if (!state.ProviderAvailable) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (udc != "RRLPFILCUR") return Results.NotFound();

        var facilityTag = "SITE.UIS::" + (facilityId == "WELL_ALARM_MISMATCH" ? "OTHER" : facilityId);
        var set = new { IsSet = true, Priority = 2, ReportedTime = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Unspecified) };
        var cleared = new { IsSet = false, Priority = 1, ReportedTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc) };
        var activeMissingTime = new { IsSet = true, Priority = 3, ReportedTime = default(DateTime) };
        var truncated = facilityId == "WELL_ALARM_TRUNCATED";
        var alarms = facilityId switch
        {
            "WELL_ALARM_EMPTY" => new[] { cleared },
            "WELL_ALARM_TRUNCATED" => Enumerable.Repeat(set, 64).ToArray(),
            "WELL_ALARM" or "WELL_ALARM_MISMATCH" => new[] { set, cleared, activeMissingTime },
            _ => null
        };
        return alarms is null ? Results.NotFound() : Results.Ok(new { FacilityTag = facilityTag, Udc = udc, Alarms = alarms, Truncated = truncated });
    })
    .WithName("GetSyntheticCygNetCasRecords");

// The command side of the simulated provider. A command changes the current value the read side serves, so reading it
// back through the ordinary read path shows whether it took effect. The provider has no idempotency of its own.
app.MapPut("/api/sim/v1/assets/{assetId}/commands/{command}",
    (string assetId, string command, SimulatorCommandRequest request, SimulatorCommandBoard commands) =>
    {
        if (string.IsNullOrWhiteSpace(request.RequestId)) return Results.BadRequest(new { code = "REQUEST_ID_REQUIRED" });
        var result = commands.Execute(assetId, command, request.RequestId, request.Value, request.Unit);
        return result.Outcome switch
        {
            SimulatorCommandOutcome.Accepted => Results.Ok(new { status = "Accepted", requestId = request.RequestId, reference = result.Reference }),
            SimulatorCommandOutcome.Rejected => Results.UnprocessableEntity(new { status = "Rejected", code = result.Code }),
            SimulatorCommandOutcome.UnknownCommand => Results.NotFound(new { status = "Rejected", code = result.Code }),
            _ => Results.Json(new { status = "Failed", code = result.Code }, statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    })
    .WithName("SendSimulatedCommand");

app.MapGet("/api/sim/v1/commands", () => SimulatorCommandBoard.Definitions)
    .WithName("ListSimulatedCommands");

app.MapGet("/api/sim/v1/commands/log", (SimulatorCommandBoard commands) => Results.Ok(commands.Log()))
    .WithName("GetSimulatedCommandLog");

app.MapPut("/api/sim/v1/commands/fault", (SetCommandFaultRequest request, SimulatorCommandBoard commands) =>
    {
        if (!Enum.TryParse<SimulatorCommandFault>(request.Fault, true, out var fault) || !Enum.IsDefined(fault))
            return Results.BadRequest(new { code = "UNKNOWN_FAULT" });
        commands.SetFault(fault, request.DelaySeconds);
        return Results.NoContent();
    })
    .WithName("SetSimulatedCommandFault");

// A stand-in for the parts of ForeSite's web services that discovery reads, so CI can exercise the whole path
// without ForeSite. It lives under its own prefix: the simulator's root address must keep answering 404 to a
// ForeSite connection, which is how the "this is not ForeSite" check works.
// The real ForeSite service issues an authenticated session cookie before inventory calls.
// This stand-in deliberately requires the cookie so CI tests the adapter's token handshake,
// rather than silently passing on a bare HTTP GET. No real Windows credentials are used here.
const string ForeSiteCiCookie = "ForeSiteCiSession";
const string ForeSiteCiValue = "ci-token";
app.MapGet("/foresite/TokenService/CreateToken", (HttpContext context) =>
{
    context.Response.Cookies.Append(ForeSiteCiCookie, ForeSiteCiValue,
        new CookieOptions { Path = "/foresite", HttpOnly = true, SameSite = SameSiteMode.Lax });
    return Results.Json(new { authenticated = true });
});

bool HasForeSiteSession(HttpContext context) =>
    context.Request.Cookies.TryGetValue(ForeSiteCiCookie, out var cookie) && cookie == ForeSiteCiValue;

app.MapGet("/foresite/SurfaceNetworkService/Asset/All", (HttpContext context) =>
    !HasForeSiteSession(context) ? Results.Unauthorized() : Results.Json(new object[]
{
    new { Id = 1, Name = "North Field" },
    new { Id = 2, Name = "South Field" }
}));

app.MapGet("/foresite/Wells/UserAssets", (HttpContext context) =>
    !HasForeSiteSession(context) ? Results.Unauthorized() : Results.Json(new object[]
{
    new { Id = 0, Name = "Not Mapped" },
    new { Id = 1, Name = "North Field" },
    new { Id = 2, Name = "South Field" }
}));

// The well list intentionally leaves PrincipalDeviceType empty. Enrichment must come from the
// control-group endpoint. Asset 99 belongs to another user and must never escape either filter.
var foreSiteWells = new[]
{
    new { Id = 101, Name = "Alpha 1", AssetId = 1, WellType = 1, Field = "Alpha", Lease = "L-1", FacilityId = 7, PrincipalDeviceType = (string?)null },
    new { Id = 102, Name = "Alpha 2", AssetId = 1, WellType = 2, Field = "Alpha", Lease = "L-1", FacilityId = 7, PrincipalDeviceType = (string?)null },
    new { Id = 103, Name = "Bravo 1", AssetId = 2, WellType = 1, Field = "Bravo", Lease = "L-2", FacilityId = 8, PrincipalDeviceType = (string?)null },
    new { Id = 999, Name = "Inaccessible well", AssetId = 99, WellType = 2, Field = "Private", Lease = "Private", FacilityId = 99, PrincipalDeviceType = (string?)null }
};
app.MapPost("/foresite/Wells/Well/Filter", (ForeSiteDiscoveryFilter filter, HttpContext context, SimulatorState state) =>
{
    if (!HasForeSiteSession(context)) return Results.Unauthorized();
    if (filter.AssetIds is not { Length: > 0 }) return Results.BadRequest();
    var allowed = filter.AssetIds.Where(id => id is 0 or 1 or 2).ToHashSet();
    if (state.Scenario == "foresite-inventory-unavailable") return Results.StatusCode(503);
    return Results.Json(foreSiteWells.Where(well => allowed.Contains(well.AssetId) &&
        (state.Scenario != "foresite-well-not-returned" || well.Id != 103)).Select(well => new {
            well.Id, well.Name, well.AssetId,
            WellType = state.Scenario == "foresite-well-type-change" && well.Id == 102 ? 1 : well.WellType,
            well.Field, well.Lease, well.FacilityId, well.PrincipalDeviceType }));
});
// Synthetic single-well configuration response. Not production ForeSite validation.
app.MapGet("/foresite/Wells/Well/{wellId:long}", (long wellId, HttpContext context) =>
{
    if (!HasForeSiteSession(context)) return Results.Unauthorized();
    if (wellId == 999) return Results.StatusCode(403);
    var well = foreSiteWells.SingleOrDefault(well => well.Id == wellId);
    return well is null ? Results.NotFound() : Results.Json(new {
        well.Id, well.Name, well.WellType, well.FacilityId, well.Field, well.Lease, well.PrincipalDeviceType,
        ChangeDate = (DateTimeOffset?)null
    }, new System.Text.Json.JsonSerializerOptions());
});
app.MapPost("/foresite/WellConfig/GetWellAutonomousControlGroupData", (ForeSiteDiscoveryFilter filter, HttpContext context, SimulatorState state) =>
{
    if (!HasForeSiteSession(context)) return Results.Unauthorized();
    if (filter.AssetIds is not { Length: > 0 }) return Results.BadRequest();
    var allowed = filter.AssetIds.Where(id => id is 0 or 1 or 2).ToHashSet();
    if (state.Scenario == "foresite-device-unavailable") return Results.StatusCode(503);
    return Results.Json(new { Units = (string?)null, Values = foreSiteWells.Where(well => allowed.Contains(well.AssetId))
        .Select(well => new { WellId = well.Id, DeviceType = state.Scenario == "foresite-device-change" && well.Id == 102 ? "Replacement" : well.Id == 102 ? "ESP" : "RPC" }) });
});

// Synthetic cases exercise the recorded ForeSite field shape without copying a provider record.
app.MapGet("/foresite/WellTests/GetLatestWellTestData/{wellId:long}", (long wellId, HttpContext context, SimulatorState state) =>
{
    if (!HasForeSiteSession(context)) return Results.Unauthorized();
    if (wellId == 103) return Results.NoContent();
    if (wellId is not (101 or 102)) return Results.NotFound();
    return Results.Json(new
    {
        Id = 8000 + wellId,
        WellId = state.Scenario == "foresite-well-test-wrong-well" ? wellId + 1 : wellId,
        SampleDate = "2025-02-01T07:00:00-06:00", TestEndDate = "2025-02-01T08:00:00-06:00",
        Status = wellId == 102 ? "Review" : null,
        SPTCode = wellId == 102 ? (int?)9 : null, SPTCodeDescription = wellId == 102 ? "RejectedTest" : null,
        ValidationStatus = wellId == 102 ? "Oil rate above acceptance limit" : null,
        Oil = 23.0m, Water = wellId == 102 ? (decimal?)0 : null
    }, new System.Text.Json.JsonSerializerOptions());
});

// Synthetic current alarms shaped like the recorded ForeSite response.
app.MapGet("/foresite/Alarms/CurrentAlarms/{wellId:long}", (long wellId, HttpContext context) =>
{
    if (!HasForeSiteSession(context)) return Results.Unauthorized();
    if (wellId is < 101 or > 105) return Results.NotFound();
    var numeric = new
    {
        Id = 9001, WellId = wellId == 102 ? 101 : wellId, AlarmTypeId = 1,
        StartTime = wellId == 105 ? "2023-04-11T12:09:35" : "2023-04-11T12:09:35.5330000-05:00",
        NumericValue = (decimal?)0, StringValue = (string?)null,
        AlarmType = new { Id = 1, AlarmType = "High Rod Stress", IsDevationAlarm = false, IsAlarmManuallySuppressed = (bool?)null }
    };
    var text = new
    {
        Id = 9002, WellId = wellId, AlarmTypeId = 405, StartTime = "2023-04-11T17:09:38.0330000Z",
        NumericValue = (decimal?)null, StringValue = (string?)"Run Time 7 days (98)",
        AlarmType = new { Id = 405, AlarmType = "Pattern Card Match", IsDevationAlarm = true, IsAlarmManuallySuppressed = (bool?)true }
    };
    var records = wellId switch
    {
        103 => [],
        104 => Enumerable.Repeat(numeric, 65).ToArray(),
        _ => new[] { numeric, text }
    };
    return Results.Json(records, new System.Text.Json.JsonSerializerOptions());
});

// Synthetic saved setpoints, served only for the passive scan option.
app.MapGet("/foresite/Surveillance/Well/AllSavedSetPointParams", (long wellId, int scanOption, HttpContext context) =>
{
    if (!HasForeSiteSession(context)) return Results.Unauthorized();
    if (scanOption != 0 || wellId is < 101 or > 105) return Results.BadRequest();
    var visible = new
    {
        ElementId = "IdleTime", Description = "Idle Time Following Pump Off", Value = "00:01:00", OriginalValue = "60",
        UnitKey = "s", OriginalUnitKey = "s", RangeLow = 0L, RangeHigh = 4294967295L, IsReadOnly = false, IsHidden = false
    };
    var hidden = new
    {
        ElementId = "HiddenParameter", Description = "Hidden Parameter", Value = "9", OriginalValue = "9",
        UnitKey = "s", OriginalUnitKey = "s", RangeLow = 0L, RangeHigh = 10L, IsReadOnly = true, IsHidden = true
    };
    var group = new
    {
        WellId = wellId == 102 ? 101 : wellId, DataGroupType = "SetPoints", DataGroupName = "Basic SetPoints Configuration",
        DataGroupOrdinal = (int?)null, DeviceType = "WellPilotRPOC", IsWritable = true,
        TransactionTimeStamp = "2022-09-29T11:31:27.8630000-05:00",
        ErrorMessage = wellId == 104 ? "Controller read failed" : "success",
        SetPointsList = wellId == 105 ? Enumerable.Repeat(hidden, 129).ToArray() : new[] { visible, hidden }
    };
    return Results.Json(wellId == 103 ? [] : new[] { group }, new System.Text.Json.JsonSerializerOptions());
});

// Current surveillance fixtures for imported wells with different available readings.
app.MapGet("/foresite/Surveillance/well/{wellId:long}", (long wellId, HttpContext context) =>
{
    if (!HasForeSiteSession(context)) return Results.Unauthorized();
    if (wellId == 102)
        return Results.Json(new { Value = new { CurrentSPM = 5.5m, LastGoodScanTime = DateTimeOffset.UtcNow } },
            new System.Text.Json.JsonSerializerOptions());
    if (wellId != 101) return Results.NotFound();
    return Results.Json(new { Value = new { CurrentPumpFillage = 82m, AveragePumpFillage = 64m, LastGoodScanTime = DateTimeOffset.UtcNow } },
        new System.Text.Json.JsonSerializerOptions());
});

// Stored, synthetic surface-card series. These GETs never trigger a scan or command.
app.MapGet("/foresite/Dynacards/Dynacard/LatestWithUnits/Lib/{wellId:long}/{type:int}",
    (long wellId, int type, HttpContext context) =>
    {
        if (!HasForeSiteSession(context)) return Results.Unauthorized();
        if (wellId is not (101 or 102) || type is not (1 or 2)) return Results.NotFound();
        var points = type == 1 ? ForeSiteSurfaceCardFixtures.Current : ForeSiteSurfaceCardFixtures.Full;
        return Results.Json(new
        {
            Value = new
            {
                Timestamp = DateTimeOffset.UtcNow.AddMinutes(-type),
                SurfaceCards = wellId == 102 ? [] : new[] { new { Points = points } }
            },
            Units = new { Position = new { UnitKey = "in" }, Load = new { UnitKey = "lb" } }
        }, new System.Text.Json.JsonSerializerOptions());
    });

// Synthetic daily rows with the recorded production-day boundary shape.
app.MapGet("/foresite/Surveillance/Well/DailyAverage/{wellId:long}/{start}/{end}",
    (long wellId, DateTimeOffset start, DateTimeOffset end, HttpContext context, SimulatorState state) =>
    {
        if (!HasForeSiteSession(context)) return Results.Unauthorized();
        if (wellId is not (101 or 102 or 103) || end <= start)
            return Results.BadRequest();
        var returnedWell = wellId == 102 ? 101 : wellId;
        var rows = new[]
        {
            new { Id = 7003, WellId = returnedWell, StartDateTime = "2026-11-03T06:00:00Z", EndDateTime = "2026-11-04T05:59:59.9900000Z", TotalFluidRateInferred = (decimal?)1, OilRateInferred = (decimal?)1, WaterRateInferred = (decimal?)1 },
            new { Id = 7002, WellId = returnedWell, StartDateTime = "2026-11-02T00:00:00-06:00", EndDateTime = "2026-11-02T23:59:59.9900000-06:00", TotalFluidRateInferred = (decimal?)null, OilRateInferred = (decimal?)null, WaterRateInferred = (decimal?)null },
            new { Id = 7001, WellId = returnedWell, StartDateTime = "2026-11-01T05:00:00Z", EndDateTime = "2026-11-02T05:59:59.9900000Z", TotalFluidRateInferred = (decimal?)0, OilRateInferred = (decimal?)0, WaterRateInferred = (decimal?)null }
        };
        // Let the UI exercise the rolling preview window as well as the fixed DST fixture.
        if (start >= new DateTimeOffset(2026, 11, 4, 6, 0, 0, TimeSpan.Zero))
        {
            var midnight = end.UtcDateTime.Date;
            rows = [
                new { Id = 7004, WellId = returnedWell, StartDateTime = midnight.AddDays(-1).ToString("O"),
                    EndDateTime = midnight.AddMilliseconds(-10).ToString("O"), TotalFluidRateInferred = (decimal?)420, OilRateInferred = (decimal?)250, WaterRateInferred = (decimal?)170 }
            ];
        }
        return Results.Json(new
        {
            Units = new
            {
                TotalFluidRateInferred = new { UnitKey = "STB/d" },
                OilRateInferred = new { UnitKey = "STB/d" },
                WaterRateInferred = new { UnitKey = "STB/d" }
            },
            Values = wellId == 103 || state.Scenario == "foresite-history-empty" ? [] : rows
        }, new System.Text.Json.JsonSerializerOptions());
    });

// Recorded-shape trend stand-in for the Gateway history integration tests. This is simulated provider data.
app.MapPost("/foresite/Surveillance/CygNetTrends/{wellId:long}/{start}/{end}",
    (long wellId, DateTimeOffset start, DateTimeOffset end, string[] quantities, HttpContext context, SimulatorState state) =>
    {
        if (!HasForeSiteSession(context)) return Results.Unauthorized();
        if (wellId != 101 || quantities.Length != 1 || quantities[0] is not ("CurrentPumpFillage" or "CurrentSPM") || end <= start)
            return Results.BadRequest();
        var strokeRate = quantities[0] == "CurrentSPM";
        return Results.Json(new[] { new {
            ErrorMessage = "Success", Units = strokeRate ? "SPM" : "%", PointUDC = strokeRate ? "SIMULATED-SPSTRN" : "SIMULATED-RRLPFILCUR",
            PointValues = state.Scenario == "foresite-history-empty" ? [] : new[] {
                new { Timestamp = start.ToUniversalTime(), Value = strokeRate ? 10.3m : 82m },
                new { Timestamp = start.AddMinutes(5).ToUniversalTime(), Value = strokeRate ? 11.4m : 79m } }
        } }, new System.Text.Json.JsonSerializerOptions());
    });

app.Run();

public partial class Program;

internal sealed record ForeSiteDiscoveryFilter(long[]? AssetIds);

internal static class ForeSiteSurfaceCardFixtures
{
    public static readonly int[][] Current = [[0, 2200], [5, 3100], [12, 3600], [10, 2700], [2, 1950], [0, 2200]];
    public static readonly int[][] Full = [[0, 2500], [5, 4200], [12, 4400], [10, 2700], [2, 2050], [0, 2500]];
}
