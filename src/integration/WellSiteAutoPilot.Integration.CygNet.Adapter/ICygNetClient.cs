using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>The CygNet point (UDC on the facility) that serves one logical quantity, and its engineering unit.</summary>
public sealed record CygNetPointMapping(string Udc, string Unit);

/// <summary>
/// One CygNet facility service: a domain plus the site and service that hold the facilities.
/// <see cref="PointMap"/> optionally overrides which point serves a quantity for this connection.
/// </summary>
public sealed record CygNetConnection(
    int Domain, string Site, string Service, string? FacilityFilter = null,
    IReadOnlyDictionary<string, CygNetPointMapping>? PointMap = null)
{
    /// <summary>CygNet's own spelling of the service, for example SITE1.UIS.</summary>
    public string SiteService => Site + "." + Service;

    /// <summary>The full tag of a facility in this service, for example SITE1.UIS::WELL0001.</summary>
    public string TagFor(string facilityId) => SiteService + "::" + facilityId;
}

/// <summary>A facility as CygNet reports it. Only the identity is required; <see cref="Category"/> is the CygNet facility category, used to group assets.</summary>
public sealed record CygNetFacility(string Id, string? Description = null, string? Type = null, string? Category = null);

/// <summary><see cref="Truncated"/> is true when the client stopped before the end of the service's facilities.</summary>
public sealed record CygNetFacilityList(IReadOnlyList<CygNetFacility> Facilities, bool Truncated = false);

/// <summary>A point on a facility as CygNet reports it: the UDC that says what it measures, its label, and its engineering unit (empty means unitless).</summary>
public sealed record CygNetPoint(string Udc, string Description, string Unit);

/// <summary>
/// The CygNet libraries sit behind this seam, so everything above it is built and tested without them. Implementations
/// throw <see cref="CygNetUnavailableException"/> (worded for a person), <see cref="UnauthorizedAccessException"/> or
/// <see cref="CygNetReadException"/>; anything else is treated as an unexplained failure and never shown verbatim.
/// </summary>
public interface ICygNetClient
{
    /// <summary>Reaches the facility service. Throws when it cannot.</summary>
    /// <remarks>Returns some facility ids (after the connection's filter) for the caller to sample reads on.</remarks>
    Task<IReadOnlyList<string>> CheckAsync(CygNetConnection connection, CancellationToken cancellationToken);

    Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken);

    /// <summary>Reads one current value. <paramref name="quantity"/> is the logical quantity; the client decides which point serves it.</summary>
    Task<EngineeringValue> ReadCurrentAsync(
        CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the points on one facility with their UDC, description and unit. This is the source of the quantity catalog:
    /// a quantity is available when its point exists. Clients that cannot list points keep this default.
    /// </summary>
    Task<IReadOnlyList<CygNetPoint>> ListPointsAsync(CygNetConnection connection, string facilityId, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<CygNetPoint>>(new CygNetReadException("CYGNET_CATALOG_UNSUPPORTED", "This CygNet client cannot list points."));

    /// <summary>
    /// Reads the history of one point (by UDC) between two UTC instants, oldest first. Numeric entries only; non-numeric
    /// ones are dropped. Clients that cannot read history keep this default.
    /// </summary>
    Task<CygNetHistory> ReadHistoryAsync(
        CygNetConnection connection, string facilityId, string udc, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken) =>
        Task.FromException<CygNetHistory>(new CygNetReadException("CYGNET_HISTORY_UNSUPPORTED", "This CygNet client cannot read history."));

    /// <summary>Reads the alarms CygNet holds for one facility's points, active and recently cleared. Read-only; never acknowledges or clears.</summary>
    Task<IReadOnlyList<CygNetAlarm>> ReadAlarmsAsync(CygNetConnection connection, string facilityId, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<CygNetAlarm>>(new CygNetReadException("CYGNET_ALARMS_UNSUPPORTED", "This CygNet client cannot read alarms."));
}

/// <summary>One alarm record on a point. <see cref="Priority"/> is CygNet's alarm priority number (lower is more urgent in the CygNet scheme).</summary>
public sealed record CygNetAlarm(string Udc, bool IsSet, int Priority, DateTimeOffset ReportedUtc, string? PointValue);

/// <summary>One numeric history entry. <see cref="Unreliable"/> carries CygNet's unreliable status flag.</summary>
public sealed record CygNetHistoryEntry(DateTimeOffset Timestamp, double Value, bool Unreliable);

/// <summary><see cref="Truncated"/> is true when the client stopped at its entry cap before the end of the window.</summary>
public sealed record CygNetHistory(IReadOnlyList<CygNetHistoryEntry> Entries, bool Truncated = false);

/// <summary>A reason the connection cannot be used that is already worded for a person.</summary>
public sealed class CygNetUnavailableException(string message) : Exception(message);

/// <summary>A failed read with a stable code and a fixed safe sentence.</summary>
public sealed class CygNetReadException(string code, string message, bool retryable = false, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

/// <summary>Used when the Gateway has no CygNet backend configured: every call says so, plainly.</summary>
public sealed class CygNetUnconfiguredClient : ICygNetClient
{
    public const string Message =
        "This Integration Gateway has no CygNet backend. Set Providers:CygNet:Backend to \"simulated\" for the built-in " +
        "simulated CygNet, or use a Gateway built on a CygNet machine (CYGNET_LIBRARY_PATH) with Backend \"live\".";

    public Task<IReadOnlyList<string>> CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<string>>(new CygNetUnavailableException(Message));

    public Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Task.FromException<CygNetFacilityList>(new CygNetUnavailableException(Message));

    public Task<EngineeringValue> ReadCurrentAsync(
        CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken) =>
        Task.FromException<EngineeringValue>(new CygNetReadException("CYGNET_BACKEND_UNCONFIGURED", Message));
}
