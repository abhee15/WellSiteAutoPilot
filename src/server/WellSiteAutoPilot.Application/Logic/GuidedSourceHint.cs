using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

/// <summary>
/// Marks one input as read from an external source instead of the module's primary provider. It lives beside the
/// portable manifest, never inside it: the contract says what a module needs, this says where that one input comes from.
/// An input without a hint is read from the primary provider, which is fixed when the draft is created.
/// </summary>
public sealed record GuidedSourceHint(string InputId, string ExternalSource);

public static class GuidedSourceHints
{
    public const int MaximumHints = 64;

    public static void Validate(GuidedModuleDraftContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.SourceHints is not { } hints) return;
        if (hints.Count > MaximumHints) throw Invalid("A draft can carry at most 64 external inputs.");
        var inputs = content.Manifest.DataRequirements.Select(input => input.Id).ToHashSet(StringComparer.Ordinal);
        var allowed = GuidedProviders.ExternalSourcesFor(content.Manifest.RequiredAdapterKey);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hint in hints)
        {
            if (hint is null || !inputs.Contains(hint.InputId)) throw Invalid("An external input must name an input the draft declares.");
            if (!seen.Add(hint.InputId)) throw Invalid("An input can have only one external source.");
            if (!allowed.Any(source => string.Equals(source.Key, hint.ExternalSource, StringComparison.Ordinal)))
                throw Invalid("That external source is not available for this module's provider.");
        }
    }

    private static WellSiteAutoPilotException Invalid(string message) =>
        new("GUIDED_SOURCE_HINT_INVALID", FailureKind.Validation, message);
}
