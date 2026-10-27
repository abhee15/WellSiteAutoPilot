namespace WellSiteAutoPilot.Domain.System;

public sealed record PlatformComponent(
    string Name,
    string Version,
    PlatformComponentStatus Status, string? ModuleSdkVersion = null);

public enum PlatformComponentStatus
{
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Unavailable = 3
}

public static class PlatformComponentNames
{
    public const string Server = "WellSite AutoPilot Server";
    public const string Gateway = "WellSite AutoPilot Integration Gateway";
    public const string DotNetRuntime = "WellSite AutoPilot .NET Runtime";
}
