using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Net;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Application.Recommendations;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Messaging.Nats;
using WellSiteAutoPilot.Telemetry;

namespace WellSiteAutoPilot.Infrastructure.Messaging;

public sealed partial class ExecutionResultV2Consumer(
    IServiceScopeFactory scopeFactory,
    INatsConnection connection,
    JetStreamConsumerSettings consumerSettings,
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
                    Subjects.ExecutionStartedV2,
                    Subjects.ExecutionCompletedV2,
                    Subjects.ExecutionFailedV2
                ],
                AckWait = consumerSettings.ResultAckWait,
                MaxDeliver = consumerSettings.MaxDeliver
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

            if (string.Equals(message.Subject, Subjects.ExecutionStartedV2, StringComparison.Ordinal))
            {
                var envelope = JsonSerializer.Deserialize<MessageEnvelope<ExecutionStartedV2>>(
                    message.Data, SerializerOptions) ?? throw new InvalidOperationException("Invalid started envelope.");
                using var activity = WellSiteTelemetry.StartConsumer("execution result apply", envelope.TraceParent);
                activity?.SetTag(WellSiteTelemetry.ExecutionTag, envelope.Payload.ExecutionId);
                await repository.ApplyStartedAsync(envelope.MessageId, ConsumerName,
                    envelope.Payload.ExecutionId, envelope.Payload.StartedAtUtc, cancellationToken);
            }
            else if (string.Equals(
                    message.Subject,
                    Subjects.ExecutionCompletedV2,
                    StringComparison.Ordinal))
            {
                var envelope = JsonSerializer.Deserialize<
                    MessageEnvelope<ExecutionCompletedV2>>(
                    message.Data,
                    SerializerOptions) ?? throw new InvalidOperationException(
                    "Execution V2 completed envelope could not be deserialized.");

                using var activity = WellSiteTelemetry.StartConsumer("execution result apply", envelope.TraceParent);
                activity?.SetTag(WellSiteTelemetry.ExecutionTag, envelope.Payload.ExecutionId);
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

                // Only the canonical Completed outcome may emit recommendations.
                // A retry of the same message must be able to recover a
                // materialization interrupted after the terminal commit.
                var shouldMaterialize = disposition == ExecutionResultDisposition.Applied;
                if (disposition == ExecutionResultDisposition.AlreadyProcessed)
                {
                    var canonical = await repository.GetAsync(
                        envelope.Payload.ExecutionId, cancellationToken);
                    shouldMaterialize = canonical is
                    {
                        Status: WellSiteAutoPilot.Domain.Executions.ExecutionStatus.Completed
                    } && string.Equals(canonical.ResultCode,
                        envelope.Payload.ResultCode, StringComparison.Ordinal) &&
                        string.Equals(canonical.OutputJson,
                            envelope.Payload.OutputJson, StringComparison.Ordinal);
                }

                if (shouldMaterialize)
                {
                    var materializer = scope.ServiceProvider
                        .GetRequiredService<RecommendationMaterializer>();
                    await materializer.MaterializeAsync(
                        envelope.Payload.ExecutionId,
                        envelope.Payload.OutputJson,
                        cancellationToken);
                }
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

                using var activity = WellSiteTelemetry.StartConsumer("execution result apply", envelope.TraceParent);
                activity?.SetTag(WellSiteTelemetry.ExecutionTag, envelope.Payload.ExecutionId);
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
                throw new InvalidOperationException(
                    $"Unexpected execution V2 result subject: {message.Subject}");
            }

            await message.AckAsync(cancellationToken: cancellationToken);
        }
    }

    private void ObserveResult(ExecutionResultDisposition disposition,
        Guid executionId, Guid messageId, string incomingStatus)
    {
        ExecutionResultDiagnostics.Record(disposition, "v2", incomingStatus);
        if (disposition == ExecutionResultDisposition.ConflictingTerminalResult)
        {
            LogTerminalConflict(logger, executionId, messageId, incomingStatus);
        }
    }

    [LoggerMessage(
        EventId = 2211,
        Level = LogLevel.Warning,
        Message = "Conflicting V2 terminal result observed for Execution {ExecutionId}, Message {MessageId}, incoming status {IncomingStatus}. The original terminal state remains authoritative; inspect audit event execution.result.conflict.")]
    private static partial void LogTerminalConflict(
        ILogger logger, Guid executionId, Guid messageId, string incomingStatus);

    [LoggerMessage(
        EventId = 2210,
        Level = LogLevel.Warning,
        Message = "Execution V2 result consumer session failed and will be retried.")]
    private static partial void LogConsumerFailure(
        ILogger logger,
        Exception exception);
}
