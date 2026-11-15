using System.Collections;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

namespace WellSiteAutoPilot.Integration.CygNet;

public sealed partial class CygNetApiClient : ICygNetAlarmClient
{
    public async Task<CygNetPointAlarmBatch> ReadPointAlarmsAsync(CygNetConnection connection, string facilityId, string udc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CygNetPointCatalog.IsFacilityId(facilityId) ||
            connection.TagFor(facilityId).Length > CygNetAdapter.MaximumFacilityTagLength ||
            connection.FacilityFilter is { } filter && !CygNetAdapter.Matches(filter, facilityId) ||
            !CygNetPointCatalog.IsUdc(udc))
            throw new CygNetReadException("CYGNET_ALARM_SCOPE_INVALID", "The point is outside this CygNet connection.");

        var facilityTag = connection.TagFor(facilityId);
        try
        {
            return await Run(connection, () =>
            {
                var (_, records) = GetPointAlarmRecords(connection, facilityTag, udc);
                var alarms = new List<CygNetPointAlarm>();
                foreach (dynamic record in records)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (alarms.Count == CygNetActiveAlarms.MaximumRecords)
                        return new CygNetPointAlarmBatch(facilityTag, udc, alarms, Truncated: true);

                    object tag = record.Tag;
                    if (!string.Equals((string)((dynamic)tag).UDC, udc, StringComparison.OrdinalIgnoreCase) ||
                        !CygNetActiveAlarms.MatchesScopedPointTag(facilityTag, udc, tag.ToString()))
                        throw new CygNetReadException("CYGNET_ALARM_IDENTITY_MISMATCH", "CAS returned a record for an unverified point.");

                    alarms.Add(new((bool)record.IsSet, (int)record.AlarmPriority, (DateTime)record.ReportedTime));
                }
                return new CygNetPointAlarmBatch(facilityTag, udc, alarms, Truncated: false);
            }, cancellationToken, TimeSpan.FromSeconds(15));
        }
        catch (OperationCanceledException) { throw; }
        catch (CygNetUnavailableException) { throw; }
        catch (CygNetReadException) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        catch (Exception error)
        {
            throw new CygNetReadException("CYGNET_CAS_READ_FAILED", "CygNet CAS could not provide the requested point records.", inner: error);
        }
    }

    private static (object Requested, IEnumerable Records) GetPointAlarmRecords(CygNetConnection connection, string facilityTag, string udc)
    {
        var runtime = CygNetRuntime.Require();
        var requested = runtime.PointTag(runtime.FacilityTag(facilityTag), udc);
        dynamic alarms = CygNetRuntime.AlarmClient(runtime.AssociatedService(Service(runtime, connection), "CAS"));
        return (requested, (IEnumerable)alarms.GetAlarmsForPoints(runtime.SinglePointTagList(requested)));
    }
}
