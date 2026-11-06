using WellSiteAutoPilot.Integration.ForeSite;

internal static class SimulatorChecks
{
    public static async Task RunAsync()
    {
        var simulator = new ForeSiteSimulator();
        var client = new HttpClient(simulator) { BaseAddress = new Uri("http://sim/") };
        var session = new ForeSiteSession(client);
        var cache = new ForeSiteStatusCache(TimeProvider.System, TimeSpan.FromSeconds(3));

        var spm = await ForeSiteMeasurements.ReadAsync(session, ForeSiteSimulator.WellId, "CurrentSPM", CancellationToken.None, cache);
        Check(spm.Value == 12.3m && spm.Unit == "SPM", "recorded CurrentSPM changed: " + spm.Value + " " + spm.Unit);

        var catalog = await ForeSiteCatalog.ReadAsync(session, ForeSiteSimulator.WellId, CancellationToken.None, cache);
        Check(catalog.DeviceType == "WellPilotRPOC" && catalog.Entries.Any(e => e.Key == "PumpFillage" && e.Unit == "%") &&
            !catalog.Entries.Any(e => e.Key == "StrokesYesterday"), "recorded catalog lost the device type or listed the unset stroke count");
        Check(simulator.Requests.Count(r => r.EndsWith("/Surveillance/well/776", StringComparison.Ordinal)) == 1, "simulator saw more than one status read");

        var setpoints = await ForeSiteSetpoints.ReadAsync(session, ForeSiteSimulator.WellId, CancellationToken.None);
        Check(setpoints.Any(s => s.ElementId == "POLoadSP" && s.Value == 30 && s.Unit == "%"), "recorded setpoints lost POLoadSP");

        var alarms = await ForeSiteAlarms.ReadAsync(session, ForeSiteSimulator.WellId, CancellationToken.None);
        Check(alarms.Count == 1 && alarms[0].Name == "Inferred Production Today", "recorded alarm changed");

        var daily = await ForeSiteDailyAverageReader.ReadAsync(session, ForeSiteSimulator.WellId,
            new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
        Check(daily.Days.Count == 6 && daily.Days[0].StartUtc.Hour == 5, "recorded daily averages changed");

        try { await ForeSiteSetpoints.ReadAsync(session, "999", CancellationToken.None); }
        catch (ForeSiteMeasurementException) { Console.WriteLine("ForeSite simulator checks passed."); return; }
        throw new InvalidOperationException("simulator served an unknown well");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
