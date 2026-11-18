using System.Text.Json;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>
/// Where an author wants an input read from. It lives beside the portable manifest, never inside it: the contract says
/// what a module needs, a hint says where to look. A hint is a preference unless <see cref="Required"/> is set, and the
/// parameters (a JSON object) are validated against the operation's own parameter schema when one is registered.
/// </summary>
public sealed record GuidedSourceHint(string InputId, string? AdapterKey, string? OperationKey, bool Required, string? ParametersJson = null);

public static class GuidedSourceHints
{
    public const string OperationUnavailable = "GUIDED_LIVE_OPERATION_UNAVAILABLE";
    public const int MaximumHints = 64;
    public const int MaximumParameterBytes = 4096;

    public static void Validate(GuidedModuleDraftContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.SourceHints is not { } hints) return;
        if (hints.Count > MaximumHints) throw Invalid("A draft can carry at most 64 source hints.");
        var inputs = content.Manifest.DataRequirements.Select(input => input.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hint in hints)
        {
            if (hint is null || !inputs.Contains(hint.InputId)) throw Invalid("A source hint must name an input the draft declares.");
            if (!seen.Add(hint.InputId)) throw Invalid("An input can carry only one source hint.");
            if (hint.AdapterKey is null && hint.OperationKey is null)
                throw Invalid("A source hint must name a provider or an operation.");
            if (hint.AdapterKey is not null && !IsIdentifier(hint.AdapterKey) ||
                hint.OperationKey is not null && !IsIdentifier(hint.OperationKey))
                throw Invalid("Provider and operation keys are short identifiers.");
            if (hint.ParametersJson is null) continue;
            if (hint.OperationKey is null) throw Invalid("Source parameters need an operation.");
            if (System.Text.Encoding.UTF8.GetByteCount(hint.ParametersJson) > MaximumParameterBytes)
                throw Invalid("Source parameters are too large.");
            try
            {
                using var document = JsonDocument.Parse(hint.ParametersJson, new JsonDocumentOptions { MaxDepth = 8 });
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw Invalid("Source parameters must be a JSON object.");
            }
            catch (JsonException) { throw Invalid("Source parameters are not valid JSON."); }
        }
    }

    /// <summary>
    /// Null when the hint is absent, only a preference, or satisfied. A required hint that this connection cannot honour
    /// returns the reason the author needs to act on.
    /// </summary>
    public static string? UnmetReason(GuidedSourceHint? hint, string adapterKey, IReadOnlyCollection<string> operationKeys)
    {
        if (hint is not { Required: true }) return null;
        if (hint.AdapterKey is not null && !string.Equals(hint.AdapterKey, adapterKey, StringComparison.OrdinalIgnoreCase))
            return GuidedInputSupport.AdapterMismatch;
        if (hint.OperationKey is not null && !operationKeys.Contains(hint.OperationKey, StringComparer.Ordinal))
            return OperationUnavailable;
        return null;
    }

    private static bool IsIdentifier(string value) =>
        value.Length is > 0 and <= 64 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');

    private static WellSiteAutoPilotException Invalid(string message) =>
        new("GUIDED_SOURCE_HINT_INVALID", FailureKind.Validation, message);
}
