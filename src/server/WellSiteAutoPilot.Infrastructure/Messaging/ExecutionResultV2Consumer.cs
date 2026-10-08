using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Net;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Messaging.Nats;

namespace WellSiteAutoPilot.Infrastructure.Messaging;

public sealed partial class ExecutionResultV2Consumer(
    IServiceScopeFactory scopeFactory,
    INatsConnection connection,
    ILogger<ExecutionResultV2Consumer> logger) : BackgroundService
{
    public const string ConsumerName = "wsa-server-execution-results-v2";

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeSessionAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogConsumerFailure(logger, exception);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task ConsumeSessionAsync(CancellationToken cancellationToken)
    {
        await JetStreamTopology.EnsureAsync(connection, cancellationToken);

        var jetStream = connection.CreateJetStreamContext();
        var consumer = await jetStream.CreateOrUpdateConsumerAsync(
            JetStreamTopology.ExecutionStream,
            new ConsumerConfig(ConsumerName)
            {
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                FilterSubjects =
                [
                    Subjects.ExecutionCompletedV2,
                    Subjects.ExecutionFailedV2
                ],
                AckWait = TimeSpan.FromSeconds(30),
                MaxDeliver = 5
            },
            cancellationToken);

        await foreach (var message in consumer.ConsumeAsync<string>(
            cancellationToken: cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(message.Data))
            {
                await message.AckAsync(cancellationToken: cancellationToken);
                continue;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IExecutionRepository>();

            if (string.Equals(
                    message.Subject,
                    Subjects.ExecutionCompletedV2,
                    StringComparison.Ordinal))
            {
                var envelope = JsonSerializer.Deserialize<
                    MessageEnvelope<ExecutionCompletedV2>>(
                    message.Data,
                    SerializerOptions) ?? throw new InvalidOperationException(
                    "Execution V2 completed envelope could not be deserialized.");

                await repository.ApplyCompletedAsync(
                    envelope.MessageId,
                    ConsumerName,
                    envelope.Payload.ExecutionId,
                    envelope.Payload.StartedAtUtc,
                    envelope.Payload.CompletedAtUtc,
                    envelope.Payload.ResultCode,
                    envelope.Payload.OutputJson,
                    cancellationToken);
            }
            else if (string.Equals(
                         message.Subject,
                         Subjects.ExecutionFailedV2,
                         StringComparison.Ordinal))
            {
                var envelope = JsonSerializer.Deserialize<
                    MessageEnvelope<ExecutionFailedV2>>(
                    message.Data,
                    SerializerOptions) ?? throw new InvalidOperationException(
                    "Execution V2 failed envelope could not be deserialized.");

                await repository.ApplyFailedAsync(
                    envelope.MessageId,
                    ConsumerName,
                    envelope.Payload.ExecutionId,
                    envelope.Payload.StartedAtUtc,
                    envelope.Payload.FailedAtUtc,
                    envelope.Payload.FailureCode,
                    cancellationToken);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unexpected execution V2 result subject: {message.Subject}");
            }

            await message.AckAsync(cancellationToken: cancellationToken);
        }
    }

    [LoggerMessage(
        EventId = 2210,
        Level = LogLevel.Warning,
        Message = "Execution V2 result consumer session failed and will be retried.")]
    private static partial void LogConsumerFailure(
        ILogger logger,
        Exception exception);
}
