namespace WellSiteAutoPilot.Api.Contracts.System;

public sealed record SystemInfoResponse(
    string Product,
    IReadOnlyCollection<PlatformComponentResponse> Components);

/// <summary>The platform's overall state for anyone signed in: how many services there are and how many are not healthy.</summary>
public sealed record SystemHealthResponse(
    PlatformComponentStatus Status,
    int ComponentCount,
    int UnhealthyCount);

public sealed record PlatformComponentResponse(
    string Name,
    string Version,
    PlatformComponentStatus Status);
