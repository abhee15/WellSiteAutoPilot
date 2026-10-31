using System.Globalization;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Integration.CygNet.Adapter;

/// <summary>Vendor-independent validation; the native bridge supplies the installed library's flag masks.</summary>
public static class CygNetPointEvidence
{
    public static CygNetPointValue Create(object? raw, DateTime timestamp, ulong status, ulong unreliableMask, ulong neutralMask)
    {
        if (!decimal.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value)) throw Failure("CYGNET_VALUE_NOT_NUMERIC");
        if (timestamp == default || timestamp.Kind == DateTimeKind.Unspecified) throw Failure("CYGNET_TIMESTAMP_INVALID");
        var quality = (status & unreliableMask) != 0 ? "Bad" : (status & ~neutralMask) == 0 ? "Good" : "Uncertain";
        return new(value, new DateTimeOffset(timestamp.ToUniversalTime()), quality);
    }
    private static WellSiteAutoPilotException Failure(string code) => new(code, FailureKind.DependencyUnavailable,
        "CygNet returned a point without usable numeric acquisition evidence.");
}
