using System.Diagnostics;

namespace WellSiteAutoPilot.Telemetry;

/// <summary>
/// The product's own trace source. Spans carry technical timing only; the business
/// <c>CorrelationId</c> is a separate tag and never replaces the W3C trace id.
/// </summary>
public static class WellSiteTelemetry
{
    public const string SourceName = "WellSiteAutoPilot";

    public const string CorrelationTag = "wsa.correlation_id";

    public const string ExecutionTag = "wsa.execution_id";

    public static ActivitySource Source { get; } = new(SourceName);

    /// <summary>
    /// Starts a consumer span that continues the producer's trace when the message
    /// carries a valid W3C <c>traceparent</c>, and a new trace otherwise. Returns
    /// null when nothing is listening, so callers use <c>activity?.</c>.
    /// </summary>
    public static Activity? StartConsumer(string name, string? traceParent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return !string.IsNullOrWhiteSpace(traceParent) &&
            ActivityContext.TryParse(traceParent, null, out var parent)
            ? Source.StartActivity(name, ActivityKind.Consumer, parent)
            : Source.StartActivity(name, ActivityKind.Consumer);
    }
}
