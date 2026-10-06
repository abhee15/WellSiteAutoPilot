using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Messaging.Nats;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

public sealed partial class ExecutionRequestConsumer(
    INatsConnection connection,
    GatewayDataClient gatewayDataClient,
    TimeProvider timeProvider,
    ILogger<ExecutionRequestConsumer> logger) : BackgroundService
{
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
            new ConsumerConfig("wsa-dotnet-worker-v1")
            {
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
                FilterSubject = Subjects.ExecutionRequestedV1,
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

            var envelope = JsonSerializer.Deserialize<MessageEnvelope<ExecutionRequestedV1>>(
                message.Data,
                SerializerOptions);

            if (envelope is null)
            {
                throw new InvalidOperationException(
                    "Execution request envelope could not be deserialized.");
            }

            await ProcessAsync(
                envelope,
                jetStream,
                cancellationToken);

            await message.AckAsync(cancellationToken: cancellationToken);
        }
    }

    private async Task ProcessAsync(
        MessageEnvelope<ExecutionRequestedV1> envelope,
        INatsJSContext jetStream,
        CancellationToken cancellationToken)
    {
        var startedAtUtc = timeProvider.GetUtcNow();

        try
        {
            if (!string.Equals(
                    envelope.Payload.Mode,
                    "Shadow",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new WellSiteAutoPilotException(
                    "EXECUTION_MODE_NOT_SUPPORTED",
                    FailureKind.Validation,
                    "The .NET foundation worker currently accepts Shadow executions only.");
            }

            var engineeringValue = await gatewayDataClient.GetCurrentAsync(
                envelope.Payload.AssetExternalId,
                envelope.Payload.Quantity,
                cancellationToken);

            var completedAtUtc = timeProvider.GetUtcNow();
            var result = new ExecutionCompletedV1(
                envelope.Payload.ExecutionId,
                startedAtUtc,
                completedAtUtc,
                "SHADOW_OBSERVATION_COMPLETED",
                JsonSerializer.Serialize(engineeringValue, SerializerOptions));

            await PublishResultAsync(
                Subjects.ExecutionCompletedV1,
                envelope,
                result,
                $"{envelope.Payload.ExecutionId:N}:completed:v1",
                jetStream,
                cancellationToken);

            LogExecutionCompleted(logger, envelope.Payload.ExecutionId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = FailureClassifier.Classify(exception);
            var failedAtUtc = timeProvider.GetUtcNow();

            var result = new ExecutionFailedV1(
                envelope.Payload.ExecutionId,
                startedAtUtc,
                failedAtUtc,
                failure.Code,
                failure.Kind == FailureKind.Unexpected);

            await PublishResultAsync(
                Subjects.ExecutionFailedV1,
                envelope,
                result,
                $"{envelope.Payload.ExecutionId:N}:failed:v1",
                jetStream,
                cancellationToken);

            LogExecutionFailed(
                logger,
                exception,
                envelope.Payload.ExecutionId,
                failure.Code);
        }
    }

    private static async Task PublishResultAsync<TPayload>(
        string subject,
        MessageEnvelope<ExecutionRequestedV1> request,
        TPayload result,
        string messageId,
        INatsJSContext jetStream,
        CancellationToken cancellationToken)
    {
        var resultEnvelope = new MessageEnvelope<TPayload>(
            Guid.NewGuid(),
            subject,
            1,
            DateTimeOffset.UtcNow,
            request.CorrelationId,
            request.TraceParent,
            result);

        var acknowledgement = await jetStream.PublishAsync(
            subject,
            JsonSerializer.Serialize(resultEnvelope, SerializerOptions),
            opts: new NatsJSPubOpts
            {
                MsgId = messageId
            },
            cancellationToken: cancellationToken);

        acknowledgement.EnsureSuccess();
    }

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "Shadow execution {ExecutionId} completed.")]
    private static partial void LogExecutionCompleted(
        ILogger logger,
        Guid executionId);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Warning,
        Message = "Shadow execution {ExecutionId} failed with {FailureCode}.")]
    private static partial void LogExecutionFailed(
        ILogger logger,
        Exception exception,
        Guid executionId,
        string failureCode);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Warning,
        Message = "Execution consumer session failed and will be retried.")]
    private static partial void LogConsumerFailure(
        ILogger logger,
        Exception exception);
}
