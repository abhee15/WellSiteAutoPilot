using Microsoft.AspNetCore.Mvc;

namespace WellSiteAutoPilot.Http;

public sealed class WellSiteProblemDetails : ProblemDetails
{
    public string Code { get; init; } = string.Empty;

    public string CorrelationId { get; init; } = string.Empty;

    public string TraceId { get; init; } = string.Empty;

    public bool Retryable { get; init; }
}
