using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Infrastructure.Messaging;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.Worker.DotNet.Execution;

internal static class AuthorizedReadTransportChecks
{
    public static async Task RunAsync()
    {
        await using var connection = new NatsConnection(NatsConnectionOptions.Create(
            Environment.GetEnvironmentVariable("WSA_NATS_URL") ?? "nats://127.0.0.1:4222",
            Environment.GetEnvironmentVariable("WSA_NATS_TOKEN")));
        await connection.ConnectAsync();
        var repository = new Reads();
        var gateway = new Gateway();
        var services = new ServiceCollection().AddSingleton<IExecutionReadRepository>(repository)
            .AddSingleton<IExecutionMeasurementGateway>(gateway);
        await using var provider = services.BuildServiceProvider();
        using var consumer = new ExecutionMeasurementReadConsumer(provider.GetRequiredService<IServiceScopeFactory>(), connection,
            NullLogger<ExecutionMeasurementReadConsumer>.Instance);
        await consumer.StartAsync(CancellationToken.None);
        await connection.PingAsync();
        using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") };
        var client = new GatewayDataClient(http, connection);
        var input = new ExecutionInputBindingV2("fillage", Guid.NewGuid(), "forged-provider", "forged-id", "forged-quantity",
            "Current", "%", 60, false, "{}", new(repository.Plan, Guid.NewGuid(), 999, Guid.NewGuid(), "forged-kind"));
        EngineeringValue? value = null;
        // Wait for subscription registration without a fixed CI timing assumption.
        for (var attempt = 0; attempt < 25 && value is null; attempt++)
        {
            try { value = await client.GetCurrentAsync(repository.Execution, input); }
            catch (NatsNoRespondersException) { await Task.Delay(20); }
        }
        if (value?.Value != 82 || value.Quantity != "PumpFillage" || gateway.Last?.ExternalId != "42" ||
            gateway.Last.ConnectionRevision != 1 || repository.ReadCount != 1)
            throw new InvalidOperationException("The worker supplied routing authority or read plan RPC failed.");
        try
        {
            await client.GetCurrentAsync(Guid.NewGuid(), input);
            throw new InvalidOperationException("Mismatched execution was accepted.");
        }
        catch (WellSiteAutoPilotException error) when (error.Code == "EXECUTION_READ_PLAN_INVALID" && !error.Message.Contains("private", StringComparison.Ordinal)) { }
        if (gateway.ReadCount != 1) throw new InvalidOperationException("Rejected plan reached the Gateway.");
        await consumer.StopAsync(CancellationToken.None);
        Console.WriteLine("Authenticated broker read RPC uses durable IDs only, ignores forged routing, returns safe failures and excludes settings from worker replies.");
    }
    private sealed class Reads : IExecutionReadRepository
    {
        public Guid Execution { get; } = Guid.NewGuid();
        public Guid Plan { get; } = Guid.NewGuid();
        public int ReadCount { get; private set; }
        public Task<AuthorizedExecutionRead> ResolveReadAsync(Guid executionId, Guid readPlanId, CancellationToken cancellationToken = default)
        {
            if (executionId != Execution || readPlanId != Plan)
                throw new WellSiteAutoPilotException("EXECUTION_READ_PLAN_INVALID", FailureKind.Authorization, "private details");
            ReadCount++;
            return Task.FromResult(new AuthorizedExecutionRead("foresite", Guid.NewGuid(), 1, "42", "PumpFillage",
                new Dictionary<string, string?> { ["baseUrl"] = "http://private/", ["credentialReference"] = "private" }));
        }
    }
    private sealed class Gateway : IExecutionMeasurementGateway
    {
        public AuthorizedExecutionRead? Last { get; private set; }
        public int ReadCount { get; private set; }
        public Task<EngineeringValue> GetCurrentAsync(AuthorizedExecutionRead read, CancellationToken cancellationToken = default)
        {
            Last = read;
            ReadCount++;
            return Task.FromResult(new EngineeringValue(read.Quantity, 82, "%", DateTimeOffset.UtcNow, "Good", "fixture"));
        }
    }
}
