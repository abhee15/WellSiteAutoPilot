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

app.MapPost("/api/sim/v1/scenarios/reset", (ResetScenarioRequest request, SimulatorState state) =>
    {
        state.Reset(request.Scenario);
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

// Simulated command endpoint for the governed Autonomous/approved-recommendation path. It never touches a real
// provider; it records the command, updates simulator state and supports readback by idempotency key.
app.MapPost("/api/sim/v1/commands", (SimulatedCommandRequest request, SimulatorState state) =>
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || string.IsNullOrWhiteSpace(request.AssetId) ||
            string.IsNullOrWhiteSpace(request.Command))
        {
            return Results.BadRequest();
        }

        try
        {
            return Results.Ok(state.ApplyCommand(request));
        }
        catch (SimulatorProviderUnavailableException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("SendSimulatedCommand")
    .Produces<SimulatedCommandResult>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/sim/v1/commands/{idempotencyKey}", (string idempotencyKey, SimulatorState state) =>
    {
        try
        {
            return Results.Ok(state.ReadbackCommand(idempotencyKey));
        }
        catch (SimulatorProviderUnavailableException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("ReadbackSimulatedCommand")
    .Produces<SimulatedCommandResult>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/sim/v1/commands", (SimulatorState state) => Results.Ok(state.Commands))
    .WithName("ListSimulatedCommands");

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
app.MapPost("/foresite/WellConfig/GetWellAutonomousControlGroupData", (ForeSiteDiscoveryFilter filter, HttpContext context, SimulatorState state) =>
{
    if (!HasForeSiteSession(context)) return Results.Unauthorized();
    if (filter.AssetIds is not { Length: > 0 }) return Results.BadRequest();
    var allowed = filter.AssetIds.Where(id => id is 0 or 1 or 2).ToHashSet();
    if (state.Scenario == "foresite-device-unavailable") return Results.StatusCode(503);
    return Results.Json(new { Units = (string?)null, Values = foreSiteWells.Where(well => allowed.Contains(well.AssetId))
        .Select(well => new { WellId = well.Id, DeviceType = state.Scenario == "foresite-device-change" && well.Id == 102 ? "Replacement" : well.Id == 102 ? "ESP" : "RPC" }) });
});

app.Run();

public partial class Program;

internal sealed record ForeSiteDiscoveryFilter(long[]? AssetIds);
