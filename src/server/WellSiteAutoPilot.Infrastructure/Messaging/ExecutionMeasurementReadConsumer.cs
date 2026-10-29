using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;

namespace WellSiteAutoPilot.Infrastructure.Messaging;

// Uses the deployment's authenticated broker; no new reverse HTTP credential or
// Gateway-local plan store. The reply contains a measurement or safe code only.
public sealed partial class ExecutionMeasurementReadConsumer(IServiceScopeFactory scopeFactory,
    INatsConnection connection, ILogger<ExecutionMeasurementReadConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = connection.SubscribeAsync<string>(Subjects.ExecutionReadCurrentV1,
                    queueGroup: "wsa-server-current-reads", cancellationToken: stoppingToken);
                await Parallel.ForEachAsync(messages, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = stoppingToken },
                    async (message, ct) =>
                    {
                        if (string.IsNullOrEmpty(message.ReplyTo)) return;
                        ExecutionReadReplyV1 reply;
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        deadline.CancelAfter(TimeSpan.FromSeconds(8));
                        try
                        {
                            if (connection.ServerInfo?.AuthRequired != true)
                                throw new WellSiteAutoPilotException("EXECUTION_READ_BROKER_AUTH_REQUIRED", FailureKind.Authorization,
                                    "Authorized reads require an authenticated broker.");
                            if (message.Data is null || message.Data.Length > 256) throw new JsonException();
                            var request = JsonSerializer.Deserialize<ExecutionReadCurrentV1>(message.Data, JsonOptions) ?? throw new JsonException();
                            await using var scope = scopeFactory.CreateAsyncScope();
                            var read = await scope.ServiceProvider.GetRequiredService<IExecutionReadRepository>()
                                .ResolveReadAsync(request.ExecutionId, request.ReadPlanId, deadline.Token);
                            var value = await scope.ServiceProvider.GetRequiredService<IExecutionMeasurementGateway>()
                                .GetCurrentAsync(read, deadline.Token);
                            reply = new(value, null);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (WellSiteAutoPilotException error) { reply = new(null, error.Code); }
                        catch (JsonException) { reply = new(null, "EXECUTION_READ_PLAN_INVALID"); }
                        catch (OperationCanceledException) { reply = new(null, "EXECUTION_READ_TIMEOUT"); }
                        catch (Exception) { reply = new(null, "EXECUTION_READ_UNAVAILABLE"); }
                        var serialized = JsonSerializer.Serialize(reply, JsonOptions);
                        if (global::System.Text.Encoding.UTF8.GetByteCount(serialized) > WellSiteAutoPilot.Integration.Contracts.Providers.MeasurementPayloadLimits.MaximumReplyBytes)
                            serialized = JsonSerializer.Serialize(new ExecutionReadReplyV1(null, "EXECUTION_READ_PAYLOAD_TOO_LARGE"), JsonOptions);
                        await message.ReplyAsync(serialized, cancellationToken: ct);
                    });
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                LogReadTransportFailure(logger);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
    [LoggerMessage(EventId = 2130, Level = LogLevel.Warning, Message = "The authorized measurement transport is reconnecting.")]
    private static partial void LogReadTransportFailure(ILogger logger);
}
