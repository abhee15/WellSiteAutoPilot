namespace WellSiteAutoPilot.Application.Security;

/// <summary>Directory namespace and opaque external ID form one identity. Names are metadata.</summary>
public static class DirectoryGroupIdentity
{
    public static bool IsValidDirectory(string? id) => id is { Length: >= 1 and <= 64 } &&
        (char.IsAsciiLetterLower(id[0]) || char.IsAsciiDigit(id[0])) &&
        id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '.');
    public static bool IsValidId(string? id) => id is { Length: >= 1 and <= 256 } &&
        id == id.Trim() && !id.Any(char.IsControl);
}
