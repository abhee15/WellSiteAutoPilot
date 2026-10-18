namespace WellSiteAutoPilot.Telemetry;

/// <summary>
/// Configuration section <c>Telemetry</c>. Telemetry is diagnostic only: nothing in the
/// product depends on it, and no exporter runs unless an OTLP endpoint is configured.
/// </summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    public bool Enabled { get; set; } = true;

    /// <summary>OTLP endpoint, for example the Aspire dashboard in development. Empty means no export.</summary>
    public string? OtlpEndpoint { get; set; }
}
