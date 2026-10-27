using System.Text.Json;
using WellSiteAutoPilot.Api.Contracts.Executions;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Messaging.Contracts.Execution;

namespace WellSiteAutoPilot.Server.Contracts;

public static class ExecutionProvenanceMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Called only by the already-authorized execution detail route. Do not expose
    // read-plan references, reviewed manifests, mappings or server-only authority.
    public static ExecutionProvenanceResponse? Read(ExecutionRecord execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        if (execution.RequestContractVersion != 3 || execution.RequestPayloadJson is not { Length: <= 2 * 1024 * 1024 } json) return null;
        try
        {
            var request = JsonSerializer.Deserialize<ExecutionRequestedV3>(json, JsonOptions);
            if (request?.Package?.Sha256 is not { Length: 64 } hash || !hash.All(char.IsAsciiHexDigit) ||
                request.Execution?.ExecutionId != execution.Id || request.Execution.Inputs is not { } inputs) return null;
            var connections = inputs.Where(input => input is not null && input.AssetId != Guid.Empty &&
                    input.Source is { ConnectionId: var id, ConnectionRevision: > 0 } && id != Guid.Empty)
                .GroupBy(input => (input.Source!.ConnectionId, input.Source.ConnectionRevision))
                .OrderBy(group => group.Key.ConnectionId).ThenBy(group => group.Key.ConnectionRevision)
                .Select(group => new ExecutionConnectionResponse(group.Key.ConnectionId, group.Key.ConnectionRevision,
                    group.Select(input => input.AssetId).Distinct().Count())).ToArray();
            return new(hash.ToLowerInvariant(), connections.Take(64).ToArray(), connections.Length > 64);
        }
        catch (JsonException) { return null; }
    }
}
