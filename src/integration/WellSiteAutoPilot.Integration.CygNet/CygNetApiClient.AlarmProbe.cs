using WellSiteAutoPilot.Integration.CygNet.Adapter;

namespace WellSiteAutoPilot.Integration.CygNet;

public sealed record CygNetAlarmProbeResult(int RecordCount, int SetCount, int ClearedCount, bool Truncated,
    int MatchingUdcCount, int MatchingNativeTagCount, int MatchingScopedTagTextCount,
    int MissingReportedTimeCount, IReadOnlyDictionary<string, int> ReportedTimeKinds,
    bool ComparedReferenceTime, int UtcClockMatches, int DisplayClockMatches);

public sealed partial class CygNetApiClient
{
    // The probe reports managed identity and clock evidence without returning provider identifiers or values.
    public static Task<CygNetAlarmProbeResult> ProbePointAlarmsAsync(CygNetConnection connection, string facilityId, string udc,
        DateTimeOffset? referenceTime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!CygNetPointCatalog.IsFacilityId(facilityId) ||
            connection.TagFor(facilityId).Length > CygNetAdapter.MaximumFacilityTagLength ||
            connection.FacilityFilter is { } filter && !CygNetAdapter.Matches(filter, facilityId) ||
            !CygNetPointCatalog.IsUdc(udc))
            throw new CygNetReadException("CYGNET_ALARM_PROBE_SCOPE_INVALID", "The probe needs a facility and point within the selected connection.");

        cancellationToken.ThrowIfCancellationRequested();
        return Run(connection, () =>
        {
            var facilityTag = connection.TagFor(facilityId);
            var (requested, records) = GetPointAlarmRecords(connection, facilityTag, udc);
            var evidence = new CygNetAlarmProbeEvidence(referenceTime);
            foreach (dynamic record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (evidence.Full) return evidence.Build(truncated: true);

                object tag = record.Tag;
                var time = (DateTime)record.ReportedTime;
                evidence.Add((bool)record.IsSet, time,
                    string.Equals((string)((dynamic)tag).UDC, udc, StringComparison.OrdinalIgnoreCase),
                    tag.Equals(requested),
                    CygNetActiveAlarms.MatchesScopedPointTag(facilityTag, udc, tag.ToString()));
            }
            return evidence.Build(truncated: false);
        }, cancellationToken, TimeSpan.FromSeconds(15));
    }
}

file sealed class CygNetAlarmProbeEvidence(DateTimeOffset? referenceTime)
{
    private readonly Dictionary<string, int> timeKinds = new(StringComparer.Ordinal);
    private int records;
    private int set;
    private int udcMatches;
    private int nativeTagMatches;
    private int scopedTagTextMatches;
    private int missingTime;
    private int utcClockMatches;
    private int displayClockMatches;

    public bool Full => records >= 64;

    public void Add(bool isSet, DateTime time, bool matchingUdc, bool matchingNativeTag, bool matchingScopedTagText)
    {
        records++;
        if (isSet) set++;
        if (matchingUdc) udcMatches++;
        if (matchingNativeTag) nativeTagMatches++;
        if (matchingScopedTagText) scopedTagTextMatches++;
        if (time == default)
        {
            missingTime++;
            return;
        }
        // Compare the reported clock fields without applying a timezone to the managed value.
        if (referenceTime is { } reference)
        {
            if (time.Ticks == reference.UtcTicks) utcClockMatches++;
            if (time.Ticks == reference.Ticks) displayClockMatches++;
        }
        var kind = time.Kind.ToString();
        timeKinds[kind] = timeKinds.GetValueOrDefault(kind) + 1;
    }

    public CygNetAlarmProbeResult Build(bool truncated) =>
        new(records, set, records - set, truncated, udcMatches, nativeTagMatches, scopedTagTextMatches, missingTime, timeKinds,
            referenceTime.HasValue, utcClockMatches, displayClockMatches);
}
