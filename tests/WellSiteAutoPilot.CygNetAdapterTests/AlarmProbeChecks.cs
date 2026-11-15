using System.Text.Json;
using WellSiteAutoPilot.Integration.CygNet;
using WellSiteAutoPilot.Integration.CygNet.Adapter;
using WellSiteAutoPilot.Integration.Contracts.Providers;

internal static class AlarmProbeChecks
{
    public static async Task RejectInvalidScopeAsync()
    {
        var connection = new CygNetConnection(1, "SITE", "UIS", FacilityFilter: "WELL_*");
        foreach (var (facility, udc) in new[] { ("TANK_1", "ALARM"), ("WELL_*", "ALARM"), ("WELL_1", "*") })
        {
            try
            {
                await CygNetApiClient.ProbePointAlarmsAsync(connection, facility, udc, null, CancellationToken.None);
                throw new InvalidOperationException("The CAS probe accepted an invalid facility or point.");
            }
            catch (CygNetReadException error) when (error.Code == "CYGNET_ALARM_PROBE_SCOPE_INVALID") { }
        }
        if (CygNetAdapter.Describe(new CygNetApiClient()).ReadOperations().Any(operation => operation.Capability == ProviderCapabilityKind.Alarms))
            throw new InvalidOperationException("The CAS probe must not advertise a Gateway alarm operation.");
    }

    public static async Task<int> RunLiveAsync(string requestPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("The licensed CygNet CAS probe requires Windows.");
            return 2;
        }

        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var input = File.OpenRead(requestPath);
            var request = await JsonSerializer.DeserializeAsync<AlarmProbeRequest>(input, json, budget.Token)
                ?? throw new ArgumentException("Missing alarm probe request.");
            if (request.Settings is null || AdapterSettingsValidator.Validate(CygNetAdapter.Descriptor, request.Settings).Count != 0 ||
                CygNetAdapter.ReadConnection(request.Settings, out _) is not { } connection ||
                request.FacilityId is null || request.Udc is null)
                throw new ArgumentException("Invalid CygNet alarm probe request.");

            var result = await CygNetApiClient.ProbePointAlarmsAsync(connection, request.FacilityId, request.Udc, ParseReferenceTime(request.ReferenceReportedAt), budget.Token);
            var alarmAssemblies = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName())
                .Where(name => name.Name?.StartsWith("CygNet.", StringComparison.Ordinal) == true &&
                    name.Name.Contains("Alarms", StringComparison.Ordinal))
                .Select(name => new { name.Name, version = name.Version?.ToString() }).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(new { liveReadSucceeded = true, result, alarmAssemblies }, json));
            return 0;
        }
        catch (Exception error)
        {
            var code = error switch
            {
                CygNetReadException read => read.Code,
                CygNetNotInstalledException => "CYGNET_NOT_INSTALLED",
                CygNetUnavailableException => "CYGNET_CAS_UNAVAILABLE",
                OperationCanceledException => "CYGNET_CAS_PROBE_TIMEOUT",
                UnauthorizedAccessException => "CYGNET_CAS_PROBE_FORBIDDEN",
                TypeLoadException => "CYGNET_CAS_CLIENT_UNAVAILABLE",
                ArgumentException or JsonException or IOException => "CYGNET_CAS_PROBE_REQUEST_INVALID",
                _ => "CYGNET_CAS_PROBE_FAILED"
            };
            Console.Error.WriteLine(JsonSerializer.Serialize(new { liveReadSucceeded = false, code }, json));
            return 1;
        }
    }

    private static DateTimeOffset? ParseReferenceTime(string? text)
    {
        if (text is null) return null;
        if (!(text.EndsWith('Z') || text.Length >= 25 && text[^6] is '+' or '-' && text[^3] == ':') ||
            !DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var result))
            throw new ArgumentException("The reference timestamp needs a UTC offset.");
        return result;
    }

    private sealed record AlarmProbeRequest(Dictionary<string, string?>? Settings, string? FacilityId, string? Udc,
        string? ReferenceReportedAt);
}
