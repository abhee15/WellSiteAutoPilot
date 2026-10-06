namespace WellSiteAutoPilot.Domain.System;

public sealed record PlatformComponent(
    string Name,
    string Version,
    PlatformComponentStatus Status);

public enum PlatformComponentStatus
{
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Unavailable = 3
}
