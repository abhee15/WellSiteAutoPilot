using System.Diagnostics;
using System.Diagnostics.Metrics;
using WellSiteAutoPilot.Application.Executions;

namespace WellSiteAutoPilot.Infrastructure.Messaging;

/// <summary>Low-cardinality counters; identifiers remain in structured logs/audit.</summary>
internal static class ExecutionResultDiagnostics
{
    private static readonly Meter Meter = new("WellSiteAutoPilot.ExecutionResults");
    private static readonly Counter<long> Conflicts =
        Meter.CreateCounter<long>("wsa.execution.result.conflicts");
    private static readonly Counter<long> Replays =
        Meter.CreateCounter<long>("wsa.execution.result.replays");

    internal static void Record(
        ExecutionResultDisposition disposition, string version, string incomingStatus)
    {
        var tags = new TagList
        {
            { "contract.version", version },
            { "result.status", incomingStatus }
        };

        if (disposition == ExecutionResultDisposition.ConflictingTerminalResult)
        {
            Conflicts.Add(1, tags);
        }
        else if (disposition is
                 ExecutionResultDisposition.AlreadyProcessed or
                 ExecutionResultDisposition.MatchingTerminalResult)
        {
            Replays.Add(1, tags);
        }
    }
}
