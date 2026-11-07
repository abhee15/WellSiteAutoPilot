using System.Buffers.Binary;
using System.Text;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Integration.CygNet.Adapter;

internal static class HistoryValueChecks
{
    public static void Run()
    {
        static byte[] Int(long value) { var bytes = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(bytes, value); return bytes; }
        static byte[] Double(double value) => Int(BitConverter.DoubleToInt64Bits(value));
        Check(CygNetHistoryValues.Decode(CygNetHistoryValueKind.SignedInteger, Int(long.MaxValue)) == 9223372036854775807m,
            "Int64 precision was lost through a double conversion.");
        Check(CygNetHistoryValues.Decode(CygNetHistoryValueKind.FloatingPoint, Double(1.25)) == 1.25m &&
            CygNetHistoryValues.Decode(CygNetHistoryValueKind.Text, Encoding.UTF8.GetBytes(" -1.25e2 ")) == -125m, "Numeric decoding drifted.");
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue })
            Reject(() => CygNetHistoryValues.Decode(CygNetHistoryValueKind.FloatingPoint, Double(value)), "CYGNET_HISTORY_VALUE_INVALID");
        Reject(() => CygNetHistoryValues.Decode(CygNetHistoryValueKind.SignedInteger, new byte[7]), "CYGNET_HISTORY_VALUE_INVALID");
        Reject(() => CygNetHistoryValues.Decode(CygNetHistoryValueKind.Text, [0xff, 0xfe]), "CYGNET_HISTORY_VALUE_INVALID");
        Reject(() => CygNetHistoryValues.Decode(CygNetHistoryValueKind.Text, Encoding.UTF8.GetBytes("NaN")), "CYGNET_HISTORY_VALUE_INVALID");
        Reject(() => CygNetHistoryValues.Decode((CygNetHistoryValueKind)999, [1]), "CYGNET_HISTORY_VALUE_INVALID");
        var start = new DateTimeOffset(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);
        var request = new HistoricalReadRequest("SITE.UIS::WELL_001", "PumpFillage", start, start.AddHours(2));
        CygNetHistoryEntry Entry(int hour, long value, string? flags = "None") =>
            new(DateTime.SpecifyKind(start.AddHours(hour).UtcDateTime, DateTimeKind.Unspecified), CygNetHistoryValueKind.SignedInteger, Int(value), flags);
        var entries = new[] { Entry(2, 99), Entry(1, 70, "Unreliable"), Entry(0, 80, null), Entry(-1, 1) };
        var result = CygNetHistoryValues.ProjectReverse(request, "%", "RRLPFILCUR", entries, start.AddDays(1));
        Check(result.Samples.Count == 2 && result.Samples[0].Value == 80 && result.Samples[1].Value == 70 &&
            result.Samples[0].TimestampUtc == start && result.Samples[1].TimestampUtc == start.AddHours(1),
            "UTC, repeated DST hours, ordering or exclusive end changed.");
        Check(result.Samples[0].Quality == "Unknown" && result.Samples[1].Quality == "Bad" && !result.Truncated,
            "Missing reliability was promoted to Good or Unreliable was lost.");
        Check(result.SourcePoint == "RRLPFILCUR" && result.Aggregation == "Raw" && result.Samples.All(value =>
            value.Unit == "%" && value.Source == "cygnet:history" && value.RetrievedAtUtc == start.AddDays(1)), "History provenance or retrieval time was lost.");
        var flagged = CygNetHistoryValues.ProjectReverse(request, "%", "UDC", [Entry(1, 1), Entry(0, 2, "ConfigurableBit1")], start);
        Check(flagged.Samples[0].Quality == "Uncertain" && flagged.Samples[1].Quality == "Good", "Source reliability flags were not preserved.");
        var limited = CygNetHistoryValues.ProjectReverse(request with { MaximumSamples = 1 }, "%", "RRLPFILCUR", entries, start);
        Check(limited.Truncated && limited.Samples[0].Value == 70 && limited.Diagnostics.Contains("LATEST_SAMPLES_ONLY"),
            "Truncation did not identify the newest subset.");
        Reject(() => CygNetHistoryValues.ProjectReverse(request, "%", "UDC", [Entry(0, 1), Entry(1, 2)], start), "CYGNET_HISTORY_ORDER_INVALID");
        Reject(() => CygNetHistoryValues.ProjectReverse(request, "%", "UDC", [Entry(0, 1) with { Timestamp = DateTime.SpecifyKind(start.UtcDateTime, DateTimeKind.Local) }], start), "CYGNET_HISTORY_TIMESTAMP_INVALID");
        Reject(() => CygNetHistoryValues.ProjectReverse(request, "%", "UDC", [Entry(0, 1) with { Timestamp = default }], start), "CYGNET_HISTORY_TIMESTAMP_INVALID");
        var outside = Enumerable.Range(1, CygNetHistoryValues.MaximumScannedEntries + 1).Select(index => Entry(0, 1) with { Timestamp = start.AddSeconds(-index).UtcDateTime });
        Reject(() => CygNetHistoryValues.ProjectReverse(request, "%", "UDC", outside, start), "CYGNET_HISTORY_SOURCE_LIMIT");
        var padded = Encoding.UTF8.GetBytes(new string(' ', MeasurementPayloadLimits.MaximumValueBytes - 1) + "1");
        Reject(() => CygNetHistoryValues.ProjectReverse(request, "%", "UDC",
            [Entry(-1, 1) with { Value = new byte[MeasurementPayloadLimits.MaximumValueBytes + 1] }], start), "CYGNET_HISTORY_VALUE_INVALID");
        var oversizedSource = Enumerable.Range(0, 17).Select(index => Entry(1, 1) with { Kind = CygNetHistoryValueKind.Text, Value = padded, Timestamp = start.AddHours(1).AddSeconds(-index).UtcDateTime });
        Reject(() => CygNetHistoryValues.ProjectReverse(request, "%", "UDC", oversizedSource, start), "CYGNET_HISTORY_SOURCE_LIMIT");
        var oversizedReply = Enumerable.Range(0, 1000).Select(index => Entry(1, long.MaxValue) with { Timestamp = start.AddHours(1).AddSeconds(-index).UtcDateTime });
        Reject(() => CygNetHistoryValues.ProjectReverse(request with { Quantity = new string('Q', 64) }, "configured-u", "UDC", oversizedReply, start), "CYGNET_PAYLOAD_TOO_LARGE");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { CygNetHistoryValues.ProjectReverse(request, "%", "UDC", [], start, cancelled.Token); throw new InvalidOperationException("Cancellation ignored."); }
        catch (OperationCanceledException) { }
        Console.WriteLine("VHS history projection: strict binary/UTF8 values, exact Int64, UTC/DST, status, ordering, newest-subset truncation, source/reply bounds and cancellation passed using synthetic wire entries. Native libraries were not exercised.");
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string code)
    {
        try { action(); }
        catch (CygNetReadException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected history refusal: " + code);
    }
}
