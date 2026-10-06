namespace WellSiteAutoPilot.Api.Contracts.System;

public sealed record SystemInfoResponse(
    string Product,
    IReadOnlyCollection<PlatformComponentResponse> Components);

public sealed record PlatformComponentResponse(
    string Name,
    string Version,
    string Status);
