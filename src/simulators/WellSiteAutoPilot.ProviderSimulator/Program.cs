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

app.MapGet("/foresite/Wells/All", (HttpContext context) =>
    !HasForeSiteSession(context) ? Results.Unauthorized() : Results.Json(new object[]
{
    new { Id = 101, Name = "Alpha 1", AssetId = 1, WellType = 1, Field = "Alpha", Lease = "L-1", FacilityId = 7, PrincipalDeviceType = "RPC" },
    new { Id = 102, Name = "Alpha 2", AssetId = 1, WellType = 2, Field = "Alpha", Lease = "L-1", FacilityId = 7, PrincipalDeviceType = "ESP" },
    new { Id = 103, Name = "Bravo 1", AssetId = 2, WellType = 1, Field = "Bravo", Lease = "L-2", FacilityId = 8, PrincipalDeviceType = "RPC" }
}));

app.Run();

public partial class Program;
