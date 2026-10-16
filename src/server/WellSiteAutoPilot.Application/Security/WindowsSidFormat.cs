using System.Globalization;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>Platform-independent syntax validation; syntax alone is not identity proof.</summary>
public static class WindowsSidFormat
{
    // MS-DTYP 2.4.2.1/2.4.2.2: canonical revision-1 SID string,
    // 48-bit authority (decimal below 2^32, otherwise 12 hex digits),
    // and 1..15 unsigned 32-bit subauthorities. No platform-specific API
    // is needed to validate the trusted host's serialized identity on Linux.
    public static bool IsValid(string? sid)
    {
        if (sid is null || sid.Length > 252) return false;
        var parts = sid.Split('-');
        if (parts.Length is < 4 or > 18 ||
            !string.Equals(parts[0], "S", StringComparison.OrdinalIgnoreCase) ||
            parts[1] != "1") return false;
        var authority = parts[2];
        if (authority.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (authority.Length != 14 ||
                !ulong.TryParse(authority.AsSpan(2), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out var value) || value < 0x100000000)
                return false;
        }
        else if (!IsDecimalUInt32(authority)) return false;
        return parts.Skip(3).All(IsDecimalUInt32);
    }

    private static bool IsDecimalUInt32(string value) =>
        value.Length is >= 1 and <= 10 &&
        (value.Length == 1 || value[0] != '0') &&
        value.All(char.IsAsciiDigit) &&
        uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

}
