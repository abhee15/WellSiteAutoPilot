namespace WellSiteAutoPilot.Persistence.Executions;

public sealed class ExecutionEntity
{
    public Guid Id { get; init; }
    public Guid LogicInstanceId { get; init; }
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleVersion { get; init; } = string.Empty;
    public Guid ConfigurationRevisionId { get; init; }
    public Guid AssetId { get; init; }
    public string AssetExternalId { get; init; } = string.Empty;
    public string Quantity { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? ResultCode { get; set; }
    public string? FailureCode { get; set; }
    public string? OutputJson { get; set; }
}
