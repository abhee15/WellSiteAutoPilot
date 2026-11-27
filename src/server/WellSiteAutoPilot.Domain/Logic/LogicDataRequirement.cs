using System.Text.Json.Serialization;

namespace WellSiteAutoPilot.Domain.Logic;

/// <summary>One value a module reads. Alias is the name its rules use when the quantity alone is ambiguous, for example a current and a daily reading of the same field.</summary>
public sealed record LogicDataRequirement(
    string Id,
    string AssetRole,
    string Quantity,
    string Access,
    string? CanonicalUnit,
    int? MaximumAgeSeconds,
    bool AllowUncertainQuality,
    LogicValueSchema? ValueSchema = null,
    string? RequiredCapability = null,
    string? Alias = null)
{
    [JsonIgnore]
    public string Name => string.IsNullOrWhiteSpace(Alias) ? Quantity : Alias;
}
