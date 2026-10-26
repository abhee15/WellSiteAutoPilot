using System.Text.Json;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Messaging.Contracts;
using WellSiteAutoPilot.Messaging.Contracts.Execution;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

public sealed record DecodedExecutionRequest(MessageEnvelope<ExecutionRequestedV2> Envelope, ExecutionPackageV3? Package);

public static class ExecutionRequestReader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static DecodedExecutionRequest Read(string subject, string data)
    {
        var envelope = JsonSerializer.Deserialize<MessageEnvelope<JsonElement>>(data, Options);
        if (envelope is null || envelope.MessageType != subject) throw Invalid();
        ExecutionRequestedV2? request;
        ExecutionPackageV3? package = null;
        if (subject == Subjects.ExecutionRequestedV2 && envelope.ContractVersion == 2)
            request = envelope.Payload.Deserialize<ExecutionRequestedV2>(Options);
        else if (subject == Subjects.ExecutionRequestedV3 && envelope.ContractVersion == 3)
        {
            var hosted = envelope.Payload.Deserialize<ExecutionRequestedV3>(Options);
            request = hosted?.Execution;
            package = hosted?.Package ?? throw Invalid();
            if (package.Sha256 is not { Length: 64 } hash || !hash.All(char.IsAsciiHexDigit) ||
                string.IsNullOrWhiteSpace(package.ReviewedManifestJson)) throw Invalid();
        }
        else throw Invalid();
        if (request is null || request.Assets is null || request.Inputs is null ||
            request.Assets.Any(item => item is null) || request.Inputs.Any(item => item is null)) throw Invalid();
        return new DecodedExecutionRequest(new MessageEnvelope<ExecutionRequestedV2>(envelope.MessageId, subject,
            envelope.ContractVersion, envelope.OccurredAtUtc, envelope.CorrelationId, envelope.TraceParent, request), package);
    }

    private static WellSiteAutoPilotException Invalid() => new("EXECUTION_REQUEST_INVALID", FailureKind.Validation,
        "The execution request does not match its message contract.");
}
