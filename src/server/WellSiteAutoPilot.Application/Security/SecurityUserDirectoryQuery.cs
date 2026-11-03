using System.Globalization;
using System.Text;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Security;

/// <summary>A stable, opaque keyset position in user-creation order (newest first).</summary>
public sealed record SecurityUserDirectoryCursor(DateTimeOffset CreatedAtUtc, Guid Id)
{
    public string Encode()
    {
        var value = string.Create(CultureInfo.InvariantCulture, $"{CreatedAtUtc.UtcTicks}:{Id:N}");
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? token, out SecurityUserDirectoryCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(token) || token.Length > 128 ||
            token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) return false;
        var padded = token.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        byte[] bytes;
        try { bytes = Convert.FromBase64String(padded); }
        catch (FormatException) { return false; }
        var parts = Encoding.ASCII.GetString(bytes).Split(':');
        if (parts.Length != 2 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
            ticks < 0 || ticks > DateTimeOffset.MaxValue.UtcTicks ||
            parts[1].Length != 32 || !Guid.TryParseExact(parts[1], "N", out var id)) return false;
        cursor = new SecurityUserDirectoryCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return cursor.Encode() == token;
    }
}

/// <summary>
/// All filters apply on the protected server-side query before pagination. Direct assignments
/// mean usable direct paired grants, not Windows or other directory-group access.
/// </summary>
public sealed record SecurityUserDirectoryQuery(
    int Limit = SecurityUserDirectoryQuery.DefaultLimit,
    SecurityUserDirectoryCursor? After = null,
    string? Search = null,
    string AccountStatus = "all",
    string DirectAssignments = "all")
{
    public const int DefaultLimit = 50;
    public const int MaximumLimit = 100;
    public const int MaximumSearchLength = 100;

    public void EnsureValid()
    {
        if (Limit is < 1 or > MaximumLimit ||
            AccountStatus is not ("all" or "active" or "inactive") ||
            DirectAssignments is not ("all" or "with" or "without") ||
            (Search is { } search && (search.Length is < 1 or > MaximumSearchLength ||
                search != search.Trim() || search.Any(char.IsControl))))
            throw new WellSiteAutoPilotException(FailureCodes.ValidationFailed, FailureKind.Validation,
                "The user directory search, filters or page limit is invalid.");
    }
}

/// <summary>One filtered, authorized page. A null Next means the list is exhausted.</summary>
public sealed record SecurityUserDirectoryPage(
    IReadOnlyList<SecurityUserDirectoryEntry> Items, SecurityUserDirectoryCursor? Next);
