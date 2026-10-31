using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

public sealed record CygNetPointValue(decimal Value, DateTimeOffset TimestampUtc, string Quality);
public interface ICygNetPointReader
{
    Task<CygNetPointValue> ReadAsync(CygNetConnection connection, string facilityTag, string udc, CancellationToken cancellationToken);
}
public sealed class CygNetUnsupportedPointReader : ICygNetPointReader
{
    public Task<CygNetPointValue> ReadAsync(CygNetConnection connection, string facilityTag, string udc, CancellationToken cancellationToken) =>
        Task.FromException<CygNetPointValue>(new WellSiteAutoPilotException("CYGNET_RUNTIME_UNAVAILABLE", FailureKind.DependencyUnavailable,
            CygNetUnsupportedFacilityReader.Message));
}
public sealed class CygNetConnectionMeasurements(ICygNetPointReader reader, TimeProvider clock)
{
    public async Task<EngineeringValue> ReadAsync(ConnectionMeasurementRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ConnectionId == Guid.Empty || request.ConnectionRevision < 1 || request.Settings is null ||
            AdapterSettingsValidator.Validate(CygNetAdapter.Descriptor, request.Settings).Count != 0 ||
            CygNetAdapter.ReadConnection(request.Settings, out _) is not { } connection)
            throw Failure("CYGNET_CONNECTION_INVALID");
        if (!connection.Contains(request.ExternalId)) throw Failure("CYGNET_FACILITY_INVALID");
        var mappings = CygNetAdapter.ReadMappings(request.Settings);
        if (mappings is null || !mappings.TryGetValue(request.Quantity, out var mapping)) throw Failure("CYGNET_QUANTITY_UNSUPPORTED");
        try
        {
            var value = await reader.ReadAsync(connection, request.ExternalId, mapping.Udc, cancellationToken);
            if (value.TimestampUtc == default || value.TimestampUtc.Offset != TimeSpan.Zero) throw Failure("CYGNET_TIMESTAMP_INVALID");
            if (value.Quality is not ("Good" or "Bad" or "Uncertain")) throw Failure("CYGNET_QUALITY_INVALID");
            return new(request.Quantity, value.Value, mapping.Unit, value.TimestampUtc, value.Quality, CygNetAdapter.Key, clock.GetUtcNow());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw Failure("CYGNET_READ_TIMEOUT"); }
        catch (WellSiteAutoPilotException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { throw new WellSiteAutoPilotException("CYGNET_READ_FAILED", FailureKind.DependencyUnavailable,
            "CygNet could not provide the mapped point.", innerException: e); }
    }
    private static WellSiteAutoPilotException Failure(string code) => new(code, FailureKind.DependencyUnavailable,
        "The saved CygNet connection did not provide a verified measurement.");
}
