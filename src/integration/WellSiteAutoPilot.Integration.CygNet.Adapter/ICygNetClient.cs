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

/// <summary>
/// The CygNet libraries sit behind this seam, so everything above it is built and tested without them. Implementations
/// throw <see cref="CygNetUnavailableException"/> (worded for a person), <see cref="UnauthorizedAccessException"/> or
/// <see cref="CygNetReadException"/>; anything else is treated as an unexplained failure and never shown verbatim.
/// </summary>
public interface ICygNetClient
{
    /// <summary>Reaches the facility service. Throws when it cannot.</summary>
    Task CheckAsync(CygNetConnection connection, CancellationToken cancellationToken);

    Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken);

    /// <summary>Reads one current value. <paramref name="quantity"/> is the logical quantity; the client decides which point serves it.</summary>
    Task<EngineeringValue> ReadCurrentAsync(
        CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken);
}

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

    public Task CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Task.FromException(new CygNetUnavailableException(Message));

    public Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Task.FromException<CygNetFacilityList>(new CygNetUnavailableException(Message));

    public Task<EngineeringValue> ReadCurrentAsync(
        CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken) =>
        Task.FromException<EngineeringValue>(new CygNetReadException("CYGNET_BACKEND_UNCONFIGURED", Message));
}
