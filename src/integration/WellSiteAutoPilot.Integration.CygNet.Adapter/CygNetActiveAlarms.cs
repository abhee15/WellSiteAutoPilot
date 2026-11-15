using System.Globalization;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

public sealed record CygNetActiveAlarmRequest(
    [property: JsonRequired] string AssetExternalId,
    [property: JsonRequired] string Udc);

public sealed record CygNetPointAlarm(bool IsSet, int Priority, DateTime ReportedTime);
public sealed record CygNetPointAlarmBatch(string FacilityTag, string Udc, IReadOnlyList<CygNetPointAlarm> Alarms, bool Truncated);
public sealed record CygNetActiveAlarm(int Priority, string? ReportedTime, string? ReportedTimeKind);
public sealed record CygNetActiveAlarmResult(string AssetExternalId, string Udc, IReadOnlyList<CygNetActiveAlarm> Alarms,
    bool Truncated, DateTimeOffset RetrievedAtUtc);

public interface ICygNetAlarmClient
{
    Task<CygNetPointAlarmBatch> ReadPointAlarmsAsync(CygNetConnection connection, string facilityId, string udc,
        CancellationToken cancellationToken);
}

public static class CygNetActiveAlarms
{
    public const int MaximumRecords = 64;
    private const string OperationKey = "alarms.active-point";

    public static readonly AdapterOperationDescriptor Operation = new(
        OperationKey, "Active point alarms", "Read the set alarms for one CygNet point.",
        ProviderCapabilityKind.Alarms, "cygnet.active-point-alarms", "1.0",
        new("cygnet.active-point-alarm-request/v1", [
            new("assetExternalId", "string", true, "Exact imported facility tag."),
            new("udc", "string", true, "Point code from the facility's point catalog.")]),
        new("cygnet.active-point-alarm-result/v1", [
            new("assetExternalId", "string", true, "Exact requested facility tag."),
            new("udc", "string", true, "Requested point code."),
            new("alarms", "object[]", true, "Set CAS records in provider order."),
            new("alarms[].priority", "integer", true, "Native alarm priority."),
            new("alarms[].reportedTime", "string|null", false, "Native clock fields, with no inferred time zone."),
            new("alarms[].reportedTimeKind", "string|null", false, "Managed DateTime kind reported by CAS."),
            new("truncated", "boolean", true, "Additional CAS records were outside the inspection bound."),
            new("retrievedAtUtc", "date-time", true, "Snapshot retrieval time.")]),
        "Old reported times can still belong to set alarms. Clock fields and managed kind are preserved separately from UTC retrieval.",
        "Active means CAS reported IsSet. Cleared records are excluded.",
        new(1, 4096, 16384, TimeoutSeconds: 15, MaximumSamples: MaximumRecords),
        ResourceKind: CygNetAdapter.ResourceKind,
        Documentation: new(OperationKey, "Server-authorized Gateway read using a saved CygNet connection.",
            """{"assetExternalId":"SITE.UIS::WELL_101","udc":"RRLPFILCUR"}""",
            """{"assetExternalId":"SITE.UIS::WELL_101","udc":"RRLPFILCUR","alarms":[{"priority":2,"reportedTime":"2023-01-01T12:00:00.0000000","reportedTimeKind":"Unspecified"}],"truncated":false,"retrievedAtUtc":"2026-11-15T12:00:00Z"}""",
            "CygNet CAS Alarms.Client.GetAlarmsForPoints for one scoped PointTag."));

    internal static AdapterDescriptor Describe(AdapterDescriptor descriptor) => descriptor with
    {
        Capabilities = [.. descriptor.Capabilities, new(ProviderCapabilityKind.Alarms, "Active alarms")],
        Operations = [.. descriptor.Operations ?? [], Operation],
        OperationGroups = [.. descriptor.OperationGroups ?? [],
            new("alarms", "Alarms", "Read set CAS alarms for a specific point.", "1.0", [OperationKey])]
    };

    public static async Task<CygNetActiveAlarmResult> ReadAsync(ICygNetAlarmClient client, CygNetConnection connection,
        CygNetActiveAlarmRequest request, CancellationToken cancellationToken)
    {
        var facility = CygNetPointCatalog.ScopedFacility(connection, request.AssetExternalId);
        if (!CygNetPointCatalog.IsUdc(request.Udc))
            throw new WellSiteAutoPilotException("CYGNET_ALARM_POINT_INVALID", FailureKind.Validation, "A valid CygNet point code is required.");

        cancellationToken.ThrowIfCancellationRequested();
        CygNetPointAlarmBatch result;
        try { result = await client.ReadPointAlarmsAsync(connection, facility, request.Udc, cancellationToken); }
        catch (CygNetReadException error) { throw Failure(error.Code); }
        catch (CygNetUnavailableException) { throw Failure("CYGNET_CAS_UNAVAILABLE"); }
        catch (UnauthorizedAccessException) { throw Failure("CYGNET_CAS_FORBIDDEN"); }

        if (result is null || result.Alarms is null ||
            !string.Equals(result.FacilityTag, request.AssetExternalId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(result.Udc, request.Udc, StringComparison.OrdinalIgnoreCase) ||
            result.Alarms.Count > MaximumRecords || result.Truncated && result.Alarms.Count != MaximumRecords)
            throw Failure("CYGNET_ALARM_RESPONSE_INVALID");

        var alarms = new List<CygNetActiveAlarm>();
        foreach (var alarm in result.Alarms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (alarm is null) throw Failure("CYGNET_ALARM_RESPONSE_INVALID");
            if (!alarm.IsSet) continue;
            var time = alarm.ReportedTime;
            alarms.Add(time == default ? new(alarm.Priority, null, null) :
                new(alarm.Priority, time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture), time.Kind.ToString()));
        }
        return new(request.AssetExternalId, request.Udc, alarms, result.Truncated, DateTimeOffset.UtcNow);
    }

    public static bool MatchesScopedPointTag(string facilityTag, string udc, string? rendered)
    {
        if (string.Equals(rendered, facilityTag + "." + udc, StringComparison.OrdinalIgnoreCase)) return true;
        if (rendered is null) return false;

        // CygNet's full form is SITE.SERVICE.SHORT:LONG:FACILITY.UDC.
        var scope = facilityTag.Split("::", StringSplitOptions.None);
        var parts = rendered.Split(':');
        return scope.Length == 2 && parts.Length == 3 && parts[0].StartsWith(scope[0] + ".", StringComparison.OrdinalIgnoreCase) &&
            parts[0].Length > scope[0].Length + 1 && parts[1].Length > 0 &&
            string.Equals(parts[2], scope[1] + "." + udc, StringComparison.OrdinalIgnoreCase);
    }

    private static WellSiteAutoPilotException Failure(string code) =>
        new(code, FailureKind.DependencyUnavailable, "The selected CygNet connection could not provide the point alarms.");
}
