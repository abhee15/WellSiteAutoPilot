using System.Globalization;
using CygNet.API.Core;
using CygNet.API.Points;
using CygNet.Data.Core;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

namespace WellSiteAutoPilot.Integration.CygNet;

/// <summary>One bounded native lane shared by discovery, checks and reads; clients are reused by domain and owning service.</summary>
public sealed class CygNetRuntime : ICygNetFacilityReader, ICygNetPointReader
{
    private readonly CygNetCallGate _calls = new();
    private readonly Dictionary<CygNetConnection, RealtimeClient> _realtime = [];
    private readonly Dictionary<CygNetConnection, global::CygNet.API.Facilities.Client> _facilities = [];
    static CygNetRuntime() => CygNetAssemblyResolver.Register();

    public async Task CheckAsync(CygNetConnection connection, CancellationToken cancellationToken)
    {
        _ = await FacilitiesAsync(connection, true, cancellationToken);
    }
    public Task<CygNetFacilityBatch> ReadFacilitiesAsync(CygNetConnection connection, CancellationToken cancellationToken) =>
        FacilitiesAsync(connection, false, cancellationToken);

    private async Task<CygNetFacilityBatch> FacilitiesAsync(CygNetConnection connection, bool checkOnly, CancellationToken cancellationToken)
    {
        try
        {
            return await _calls.RunAsync(token =>
            {
                // ReadConnection restricts site/service to a narrow ASCII alphabet before this filter is built.
                if (CygNetAdapter.ReadConnection(new Dictionary<string, string?> { ["domain"] = connection.Domain.ToString(CultureInfo.InvariantCulture),
                    ["site"] = connection.Site, ["service"] = connection.Service }, out _) != connection)
                    throw new CygNetUnavailableException("The CygNet connection settings are invalid.");
                if (!_facilities.TryGetValue(connection, out var client))
                {
                    if (_facilities.Count >= 128) throw new CygNetUnavailableException("CygNet client capacity reached. Restart the Gateway after reviewing connections.");
                    var fac = new ServiceInformation().GetAssociatedService(Service(connection), ServiceType.FAC);
                    client = new global::CygNet.API.Facilities.Client(fac);
                    _facilities.Add(connection, client);
                }
                var tags = client.GetFacilityTagList($"facility_site = '{connection.Site}' AND facility_service = '{connection.Service}'", token);
                if (checkOnly)
                {
                    _ = tags.Any(); // Force a real query even if a vendor version returns a lazy sequence.
                    token.ThrowIfCancellationRequested();
                    return new CygNetFacilityBatch([], true);
                }
                var results = new List<CygNetFacilitySnapshot>();
                var complete = true;
                var examined = 0;
                foreach (var tag in tags)
                {
                    token.ThrowIfCancellationRequested();
                    if (examined++ >= CygNetAdapter.MaximumFacilities) { complete = false; break; }
                    if (!connection.Contains(tag.ToString())) { complete = false; continue; }
                    try
                    {
                        var record = client.ReadFacility(tag);
                        results.Add(new(tag.FacilityId, record.Description, record.Type));
                    }
                    catch (Exception) { complete = false; }
                }
                return new CygNetFacilityBatch(results, complete);
            }, TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e) { throw new CygNetUnavailableException("CygNet could not complete the facility query. Check domain access, licensing and service availability.", e); }
    }

    public async Task<CygNetPointValue> ReadAsync(CygNetConnection connection, string facilityTag, string udc, CancellationToken cancellationToken)
    {
        try
        {
            return await _calls.RunAsync(_ =>
            {
                if (!connection.Contains(facilityTag)) throw Failure("CYGNET_FACILITY_INVALID");
                if (!_realtime.TryGetValue(connection, out var client))
                {
                    if (_realtime.Count >= 128) throw Failure("CYGNET_CLIENT_CAPACITY");
                    client = new RealtimeClient(Service(connection));
                    _realtime.Add(connection, client);
                }
                var record = client.GetPointValueRecord(new PointTag(new FacilityTag(facilityTag), udc))
                    ?? throw Failure("CYGNET_POINT_NOT_FOUND");
                return CygNetPointEvidence.Create(record.Value, record.Timestamp, (ulong)record.Status,
                    (ulong)BaseStatusFlags.Unreliable, (ulong)(BaseStatusFlags.Initialized | BaseStatusFlags.Updated |
                        BaseStatusFlags.StringData | BaseStatusFlags.DigitalData | BaseStatusFlags.OutputData));
            }, TimeSpan.FromSeconds(6), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw Failure("CYGNET_READ_TIMEOUT"); }
        catch (WellSiteAutoPilotException) { throw; }
        catch (Exception e) { throw new WellSiteAutoPilotException("CYGNET_READ_FAILED", FailureKind.DependencyUnavailable,
            "CygNet could not provide the mapped point.", innerException: e); }
    }
    private static DomainSiteService Service(CygNetConnection c) => new(c.Domain, c.Site, c.Service);
    private static WellSiteAutoPilotException Failure(string code) => new(code, FailureKind.DependencyUnavailable,
        "CygNet could not provide a verified point value.");
}
