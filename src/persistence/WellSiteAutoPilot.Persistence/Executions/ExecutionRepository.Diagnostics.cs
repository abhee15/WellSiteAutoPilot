using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Application.Executions;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Messaging.Contracts.Execution;

namespace WellSiteAutoPilot.Persistence.Executions;

public sealed partial class ExecutionRepository
{
    public async Task<ExecutionResultDisposition> ApplyFailedWithDiagnosticsAsync(Guid messageId, string consumer,
        Guid executionId, DateTimeOffset startedAtUtc, DateTimeOffset failedAtUtc, string failureCode,
        string? diagnosticsJson, CancellationToken cancellationToken = default)
    {
        string? output = null;
        if (diagnosticsJson is { Length: > 0 and <= 32768 })
        {
            try
            {
                var items = JsonSerializer.Deserialize<ExecutionInputDiagnosticV1[]>(diagnosticsJson, SerializerOptions);
                var execution = await dbContext.Executions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == executionId, cancellationToken);
                var snapshot = execution?.RequestPayloadJson is { } json ? execution.RequestContractVersion == 3
                    ? JsonSerializer.Deserialize<ExecutionRequestedV3>(json, SerializerOptions)?.Execution
                    : execution.RequestContractVersion == 2 ? JsonSerializer.Deserialize<ExecutionRequestedV2>(json, SerializerOptions) : null : null;
                if (items is { Length: <= 64 } && snapshot?.Inputs is { } inputs)
                {
                    var safe = items.Where(item => item is not null && item.RequirementId is { Length: > 0 and <= 128 } &&
                        item.Quantity is { Length: > 0 and <= 256 } && ValidCode(item.OutcomeCode) &&
                        item.Quality is "Good" or "Uncertain" or "Bad" or "Unknown" &&
                        item.TimestampKind is null or "UnverifiedScan" or "ControllerScan" or "CardCapture" &&
                        inputs.Any(input => input.RequirementId == item.RequirementId && input.AssetId == item.AssetId &&
                            input.Quantity == item.Quantity && input.Source?.ConnectionId == item.ConnectionId &&
                            (item.Unit is null || item.Unit.Length <= 32 && item.Unit == input.CanonicalUnit)))
                        .DistinctBy(item => (item.RequirementId, item.AssetId))
                        .OrderBy(item => item.AssetId).ThenBy(item => item.RequirementId, StringComparer.Ordinal).ToArray();
                    if (safe.Length > 0) output = JsonSerializer.Serialize(new { InputDiagnostics = safe }, SerializerOptions);
                }
            }
            catch (JsonException) { /* Invalid diagnostics never prevent terminal failure persistence. */ }
        }
        return await ApplyResultAsync(messageId, consumer, executionId, ExecutionStatus.Failed,
            startedAtUtc, failedAtUtc, null, failureCode, output, cancellationToken);
    }
    private static bool ValidCode(string? code) => code is { Length: > 0 and <= 128 } && char.IsAsciiLetterUpper(code[0]) &&
        code.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '_');
}
