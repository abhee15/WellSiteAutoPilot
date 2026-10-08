using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Net;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Messaging.Nats;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

public sealed partial class ExecutionRequestV2Consumer(
    INatsConnection connection,
    ModuleExecutionEngine moduleExecutionEngine,
    TimeProvider timeProvider,
    ILogger<ExecutionRequestV2Consumer> logger) : BackgroundService
{
    public const string ConsumerName = "wsa-dotnet-worker-v2";

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
                FilterSubject = Subjects.ExecutionRequestedV2,
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

            var envelope = JsonSerializer.Deserialize<MessageEnvelope<ExecutionRequestedV2>>(
                message.Data,
                SerializerOptions) ?? throw new InvalidOperationException(
                "Execution V2 request envelope could not be deserialized.");

            await ProcessAsync(envelope, jetStream, cancellationToken);
            await message.AckAsync(cancellationToken: cancellationToken);
        }
    }

    private async Task ProcessAsync(
        MessageEnvelope<ExecutionRequestedV2> envelope,
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

            var moduleResult = await moduleExecutionEngine.ExecuteAsync(
                envelope.Payload,
                envelope.CorrelationId,
                startedAtUtc,
                cancellationToken);

            var completedAtUtc = timeProvider.GetUtcNow();
            var result = new ExecutionCompletedV2(
                envelope.Payload.ExecutionId,
                startedAtUtc,
                completedAtUtc,
                moduleResult.OutcomeCode,
                JsonSerializer.Serialize(moduleResult, SerializerOptions));

            await PublishResultAsync(
                Subjects.ExecutionCompletedV2,
                envelope,
                result,
                $"{envelope.Payload.ExecutionId:N}:completed:v2",
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

            var result = new ExecutionFailedV2(
                envelope.Payload.ExecutionId,
                startedAtUtc,
                failedAtUtc,
                failure.Code,
                failure.Kind == FailureKind.Unexpected);

            await PublishResultAsync(
                Subjects.ExecutionFailedV2,
                envelope,
                result,
                $"{envelope.Payload.ExecutionId:N}:failed:v2",
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
        MessageEnvelope<ExecutionRequestedV2> request,
        TPayload result,
        string messageId,
        INatsJSContext jetStream,
        CancellationToken cancellationToken)
    {
        var resultEnvelope = new MessageEnvelope<TPayload>(
            Guid.NewGuid(),
            subject,
            2,
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
        EventId = 1110,
        Level = LogLevel.Information,
        Message = "Shadow execution V2 {ExecutionId} completed.")]
    private static partial void LogExecutionCompleted(
        ILogger logger,
        Guid executionId);

    [LoggerMessage(
        EventId = 1111,
        Level = LogLevel.Warning,
        Message = "Shadow execution V2 {ExecutionId} failed with {FailureCode}.")]
    private static partial void LogExecutionFailed(
        ILogger logger,
        Exception exception,
        Guid executionId,
        string failureCode);

    [LoggerMessage(
        EventId = 1112,
        Level = LogLevel.Warning,
        Message = "Execution V2 consumer session failed and will be retried.")]
    private static partial void LogConsumerFailure(
        ILogger logger,
        Exception exception);
}
