using System.Text.Json;
using WellSiteAutoPilot.Domain.Executions;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Server.Contracts;

internal static class ExecutionProvenanceChecks
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static void Run()
    {
        var id = Guid.NewGuid();
        var connection = Guid.NewGuid();
        var readPlan = Guid.NewGuid();
        var assets = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var requested = new ExecutionRequestedV2(id, Guid.NewGuid(), Guid.NewGuid(), "module", "1.0.0", "Shadow", "{}", [],
            assets.Select(asset => new ExecutionInputBindingV2("fillage", asset, "foresite", "SECRET-external-id", "PumpFillage", "Current",
                "%", 60, false, "{\"SECRET\":true}", new(readPlan, connection, 2, Guid.NewGuid(), "foresite.well"))).ToArray(), DateTimeOffset.UtcNow);
        var hosted = new ExecutionRequestedV3(requested, new(new string('a', 64), "SECRET-reviewed-manifest"));
        var execution = new ExecutionRecord(id, requested.ConfiguredLogicId, requested.ModuleId, requested.ModuleVersion,
            requested.ConfigurationRevisionId, null, null, null, ExecutionMode.Shadow, ExecutionStatus.Requested,
            "provenance-check", requested.RequestedAtUtc, RequestContractVersion: 3,
            RequestPayloadJson: JsonSerializer.Serialize(hosted, JsonOptions));
        var provenance = ExecutionProvenanceMapper.Read(execution) ?? throw new InvalidOperationException("V3 provenance was not projected.");
        var json = JsonSerializer.Serialize(provenance, JsonOptions);
        if (provenance.PackageSha256 != hosted.Package.Sha256 || provenance.Connections.Count != 1 ||
            provenance.Connections.Single().AssetCount != 2 || provenance.Connections.Single().Revision != 2 ||
            json.Contains("SECRET", StringComparison.Ordinal) || json.Contains(readPlan.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException("Provenance exposed routing authority/private metadata or lost immutable connection scope.");
        foreach (var invalid in new[] { execution with { RequestContractVersion = 2 }, execution with { RequestPayloadJson = "not json" },
            execution with { RequestPayloadJson = JsonSerializer.Serialize(hosted with { Package = hosted.Package with { Sha256 = "SECRET" } }, JsonOptions) } })
            if (ExecutionProvenanceMapper.Read(invalid) is not null) throw new InvalidOperationException("Unverified provenance was projected.");
        Console.WriteLine("Execution provenance projects pinned hash/connection revision and excludes read authority, mappings and raw source metadata.");
    }
}
