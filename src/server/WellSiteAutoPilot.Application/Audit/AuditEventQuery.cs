using System.Globalization;
using System.Text;
using WellSiteAutoPilot.Domain.Audit;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Audit;

/// <summary>Position after which the next page of audit events starts (newest first).</summary>
public sealed record AuditEventCursor(DateTimeOffset OccurredAtUtc, Guid Id)
{
    /// <summary>An opaque, URL-safe token. Clients must not interpret or build it.</summary>
    public string Encode()
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{OccurredAtUtc.UtcTicks}:{Id:N}");
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(text))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? token, out AuditEventCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(token) || token.Length > 128) return false;
        var padded = token.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        byte[] bytes;
        try { bytes = Convert.FromBase64String(padded); }
        catch (FormatException) { return false; }

        var text = Encoding.ASCII.GetString(bytes);
        var parts = text.Split(':');
        if (parts.Length != 2 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
            ticks > DateTimeOffset.MaxValue.UtcTicks ||
            parts[1].Length != 32 || !Guid.TryParseExact(parts[1], "N", out var id))
            return false;

        cursor = new AuditEventCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}

/// <summary>Filters for the protected audit read. Every filter is optional and combines with AND.</summary>
public sealed record AuditEventQuery(
    int Limit = AuditEventQuery.DefaultLimit,
    AuditEventCursor? After = null,
    string? ActionPrefix = null,
    string? TargetType = null,
    Guid? ActorUserId = null,
    Guid? AssetId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null)
{
    public const int DefaultLimit = 50;
    public const int MaximumLimit = 200;
    public const int MaximumTextLength = 128;

    /// <summary>Throws a validation failure when the query is not acceptable.</summary>
    public void EnsureValid()
    {
        if (Limit is < 1 or > MaximumLimit)
            throw Invalid($"Audit event limit must be between 1 and {MaximumLimit}.");
        CheckText(ActionPrefix, "action prefix");
        CheckText(TargetType, "target type");
        if (FromUtc is { } from && ToUtc is { } to && from > to)
            throw Invalid("Audit event range start must not be after its end.");
    }

    private static void CheckText(string? value, string name)
    {
        if (value is null) return;
        if (value.Length is 0 or > MaximumTextLength || value.Any(char.IsControl))
            throw Invalid($"Audit event {name} must be 1 to {MaximumTextLength} printable characters.");
    }

    private static WellSiteAutoPilotException Invalid(string message) =>
        new(FailureCodes.ValidationFailed, FailureKind.Validation, message);
}

/// <summary>One page of audit events, newest first, with the cursor for the next page when more exist.</summary>
public sealed record AuditEventPage(IReadOnlyList<AuditEventRecord> Items, AuditEventCursor? Next);
