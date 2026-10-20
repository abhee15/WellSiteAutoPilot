namespace WellSiteAutoPilot.Http.ServiceCallers;

/// <summary>The internal callers the Integration Gateway knows, and the rights each one has.</summary>
public static class ServiceCaller
{
    public const string Server = "server";
    public const string Worker = "worker";

    public const string Scheme = "WSA-Service";
    public const string ClaimType = "wsa:service-caller";

    /// <summary>Connection management, test and discovery: the Server only. Modules run in the worker.</summary>
    public const string ServerPolicy = "InternalServerCaller";

    /// <summary>Reading provider data on behalf of an execution: the Server or the worker.</summary>
    public const string DataReadPolicy = "InternalDataReadCaller";

    public static readonly IReadOnlyList<string> All = [Server, Worker];
}
