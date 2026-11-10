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
    /// <remarks>Returns some facility ids (after the connection's filter) for the caller to sample reads on.</remarks>
    Task<IReadOnlyList<string>> CheckAsync(CygNetConnection connection, CancellationToken cancellationToken);

    Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken);

    /// <summary>Reads one current value. <paramref name="quantity"/> is the logical quantity; the client decides which point serves it.</summary>
    Task<EngineeringValue> ReadCurrentAsync(
        CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken);
}

/// <summary>A reason the connection cannot be used that is already worded for a person.</summary>
public class CygNetUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The CygNet system could not be reached from this Gateway (network, timeout or no answer).</summary>
public sealed class CygNetConnectionException(int domain, string? siteService = null, Exception? inner = null) : CygNetUnavailableException(
    siteService is null
        ? $"Connection error: a connection to CygNet domain {domain} cannot be established from this Integration Gateway. " +
          "Check that the CygNet server is running and reachable from this machine."
        : $"Connection error: a connection to {siteService} in CygNet domain {domain} cannot be established from this Integration Gateway. " +
          $"Check that the CygNet server is running and reachable from this machine, that domain {domain} is registered on this machine, " +
          "and that the site.service name is correct.", inner);

/// <summary>CygNet is not installed on the machine running this Gateway, so no CygNet domain can be reached from it.</summary>
public sealed class CygNetNotInstalledException(string? folder = null, Exception? inner = null) : CygNetUnavailableException(
    folder is null
        ? "CygNet is not installed on the server running this Integration Gateway, so it cannot connect to a CygNet domain. " +
          "Install the CygNet client components on that server (WSA does not install CygNet). If CygNet is installed in a non-standard folder, " +
          "set CygNet:LibraryPath for the Gateway (or CYGNET_LIBRARY_PATH) to the folder that holds the CygNet API."
        : $"The CygNet API found in {folder} on the server running this Integration Gateway could not be loaded, so it cannot connect to a CygNet domain. " +
          "Check that the CygNet client components are installed completely and are 64-bit.", inner);

/// <summary>A failed read with a stable code and a fixed safe sentence.</summary>
public sealed class CygNetReadException(string code, string message, bool retryable = false, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
}

/// <summary>Used where CygNet cannot run at all (a non-Windows Gateway): every call says so, plainly.</summary>
public sealed class CygNetUnconfiguredClient : ICygNetClient
{
    public const string Message =
        "CygNet connections need an Integration Gateway running on Windows on a server that has CygNet installed. " +
        "This Gateway cannot reach a CygNet domain.";

    public Task<IReadOnlyList<string>> CheckAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Task.FromException<IReadOnlyList<string>>(new CygNetUnavailableException(Message));

    public Task<CygNetFacilityList> ListFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        Task.FromException<CygNetFacilityList>(new CygNetUnavailableException(Message));

    public Task<EngineeringValue> ReadCurrentAsync(
        CygNetConnection connection, string facilityId, string quantity, CancellationToken cancellationToken) =>
        Task.FromException<EngineeringValue>(new CygNetReadException("CYGNET_BACKEND_UNCONFIGURED", Message));
}
