using WellSiteAutoPilot.Integration.Contracts.Providers;

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

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.MapGet("/api/sim/v1/assets/{assetId}/current/{quantity}",
    (string assetId, string quantity) =>
    {
        var value = new EngineeringValue(
            quantity,
            Value: 0m,
            Unit: "simulated",
            TimestampUtc: DateTimeOffset.UtcNow,
            Quality: "Good",
            Source: $"simulator:{assetId}");

        return Results.Ok(value);
    })
    .WithName("GetSimulatedCurrentValue");

app.Run();

public partial class Program;
