using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Persistence;
using WellSiteAutoPilot.Persistence.Executions;

// Test executable only: preserve the V1 outbox -> worker -> gateway journey
// without publishing an HTTP route that trusts client-authored mappings.
internal static class LegacyShadowFixture
{
    public static async Task RunAsync()
    {
        var connection = new NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("WSA_DATABASE_CONNECTION_STRING") ??
            throw new InvalidOperationException("WSA_DATABASE_CONNECTION_STRING is required."));
        if (connection.Database != "wellsite_autopilot")
            throw new InvalidOperationException("The V1 fixture requires the dedicated wellsite_autopilot CI database.");
        var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>()
            .UseNpgsql(connection.ConnectionString).Options;
        await using var db = new WellSiteAutoPilotDbContext(options);
        using var client = new HttpClient
        {
            BaseAddress = new Uri(Environment.GetEnvironmentVariable("WSA_ASSET_API_URL") ?? "http://127.0.0.1:5080"),
            Timeout = TimeSpan.FromSeconds(15)
        };

        var executionsBefore = await db.Executions.CountAsync();
        var outboxBefore = await db.OutboxMessages.CountAsync();
        var forged = new
        {
            logicInstanceId = Guid.NewGuid(), moduleId = "weatherford.sample-shadow-read", moduleVersion = "1.0.0",
            configurationRevisionId = Guid.NewGuid(), assetId = Guid.NewGuid(),
            assetExternalId = "UNAUTHORIZED-PROVIDER-TARGET", quantity = "PumpFillage"
        };
        foreach (var identity in new[] { @"CI\wsa-admin", @"CI\operator-one", @"CI\unprivileged", null })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/executions/shadow")
            {
                Content = JsonContent.Create(forged)
            };
            if (identity is not null) request.Headers.Add("X-WSA-Test-Identity", identity);
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.NotFound)
                throw new InvalidOperationException($"Retired raw dispatch returned {response.StatusCode} for {identity ?? "anonymous"}.");
        }
        if (await db.Executions.CountAsync() != executionsBefore || await db.OutboxMessages.CountAsync() != outboxBefore)
            throw new InvalidOperationException("Retired raw HTTP dispatch created an execution or outbox message.");
        using var openApi = await client.GetAsync("/swagger/v1/swagger.json");
        openApi.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync());
        if (document.RootElement.GetProperty("paths").TryGetProperty("/api/v1/executions/shadow", out _))
            throw new InvalidOperationException("Retired raw dispatch remains in OpenAPI.");

        // Use a real persisted Asset so this fixture also works with scoped
        // result reads after grant cutover. Only test code supplies the mapping.
        var assetId = await db.Assets.AsNoTracking().Where(asset => asset.IsActive)
            .OrderBy(asset => asset.Id).Select(asset => asset.Id).FirstAsync();
        var execution = await new ExecutionService(new ExecutionRepository(db), TimeProvider.System)
            .RequestShadowAsync(new ShadowExecutionCommand(Guid.NewGuid(), "weatherford.sample-shadow-read", "1.0.0",
                Guid.NewGuid(), assetId, "WELL-101", "PumpFillage"), "ci-shadow-e2e");
        Console.WriteLine(JsonSerializer.Serialize(new { executionId = execution.Id }));
    }
}
