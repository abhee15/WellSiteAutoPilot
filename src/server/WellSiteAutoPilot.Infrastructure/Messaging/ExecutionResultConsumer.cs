using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.JetStream.Models;
using NATS.Client.Core;
using NATS.Net;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Messaging.Nats;

namespace WellSiteAutoPilot.Infrastructure.Messaging;

public sealed partial class ExecutionResultConsumer(
    IServiceScopeFactory scopeFactory,
    INatsConnection connection,
    JetStreamConsumerSettings consumerSettings,
    ILogger<ExecutionResultConsumer> logger) : BackgroundService
{
    public const string ConsumerName = "wsa-server-execution-results-v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
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
                FilterSubjects = [Subjects.ExecutionCompletedV1, Subjects.ExecutionFailedV1],
                AckWait = consumerSettings.ResultAckWait,
                MaxDeliver = consumerSettings.MaxDeliver
            },
            cancellationToken);

        await foreach (var message in consumer.ConsumeAsync<string>(cancellationToken: cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(message.Data))
            {
                await message.AckAsync(cancellationToken: cancellationToken);
                continue;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IExecutionRepository>();

            if (string.Equals(message.Subject, Subjects.ExecutionCompletedV1, StringComparison.Ordinal))
            {
                var envelope = JsonSerializer.Deserialize<MessageEnvelope<ExecutionCompletedV1>>(
                    message.Data,
                    SerializerOptions) ?? throw new InvalidOperationException(
                    "Execution completed envelope could not be deserialized.");

                var disposition = await repository.ApplyCompletedAsync(
                    envelope.MessageId,
                    ConsumerName,
                    envelope.Payload.ExecutionId,
                    envelope.Payload.StartedAtUtc,
                    envelope.Payload.CompletedAtUtc,
                    envelope.Payload.ResultCode,
                    envelope.Payload.OutputJson,
                    cancellationToken);
                ObserveResult(disposition, envelope.Payload.ExecutionId,
                    envelope.MessageId, "Completed");
            }
            else if (string.Equals(message.Subject, Subjects.ExecutionFailedV1, StringComparison.Ordinal))
            {
                var envelope = JsonSerializer.Deserialize<MessageEnvelope<ExecutionFailedV1>>(
                    message.Data,
                    SerializerOptions) ?? throw new InvalidOperationException(
                    "Execution failed envelope could not be deserialized.");

                var disposition = await repository.ApplyFailedAsync(
                    envelope.MessageId,
                    ConsumerName,
                    envelope.Payload.ExecutionId,
                    envelope.Payload.StartedAtUtc,
                    envelope.Payload.FailedAtUtc,
                    envelope.Payload.FailureCode,
                    cancellationToken);
                ObserveResult(disposition, envelope.Payload.ExecutionId,
                    envelope.MessageId, "Failed");
            }
            else
            {
                throw new InvalidOperationException($"Unexpected execution result subject: {message.Subject}");
            }

            await message.AckAsync(cancellationToken: cancellationToken);
        }
    }

    private void ObserveResult(ExecutionResultDisposition disposition,
        Guid executionId, Guid messageId, string incomingStatus)
    {
        ExecutionResultDiagnostics.Record(disposition, "v1", incomingStatus);
        if (disposition == ExecutionResultDisposition.ConflictingTerminalResult)
        {
            LogTerminalConflict(logger, executionId, messageId, incomingStatus);
        }
    }

    [LoggerMessage(
        EventId = 2201,
        Level = LogLevel.Warning,
        Message = "Conflicting V1 terminal result observed for Execution {ExecutionId}, Message {MessageId}, incoming status {IncomingStatus}. The original terminal state remains authoritative; inspect audit event execution.result.conflict.")]
    private static partial void LogTerminalConflict(
        ILogger logger, Guid executionId, Guid messageId, string incomingStatus);

    [LoggerMessage(
        EventId = 2200,
        Level = LogLevel.Warning,
        Message = "Execution result consumer session failed and will be retried.")]
    private static partial void LogConsumerFailure(ILogger logger, Exception exception);
}
