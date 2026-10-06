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

app.Run();

public partial class Program;
