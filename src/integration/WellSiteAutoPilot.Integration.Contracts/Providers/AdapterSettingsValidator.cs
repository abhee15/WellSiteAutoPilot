using System.Globalization;

namespace WellSiteAutoPilot.Integration.Contracts.Providers;

/// <summary>Checks connection settings against what an adapter declared. Pure, so Server and tests can share it.</summary>
public static class AdapterSettingsValidator
{
    public static IReadOnlyList<AdapterSettingProblem> Validate(
        AdapterDescriptor descriptor, IReadOnlyDictionary<string, string?> settings)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(settings);
        var problems = new List<AdapterSettingProblem>();

        foreach (var name in settings.Keys)
            if (!descriptor.Fields.Any(field => string.Equals(field.Key, name, StringComparison.Ordinal)))
                problems.Add(new(name, "This setting is not used by this adapter."));

        foreach (var field in descriptor.Fields)
        {
            settings.TryGetValue(field.Key, out var raw);
            var value = raw?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                if (field.Required) problems.Add(new(field.Key, "A value is required."));
                continue;
            }

            if (value.Length > field.MaxLength)
            {
                problems.Add(new(field.Key, $"Use at most {field.MaxLength} characters."));
                continue;
            }

            if (value.Any(char.IsControl))
            {
                problems.Add(new(field.Key, "Control characters are not allowed."));
                continue;
            }

            var problem = field.Kind switch
            {
                AdapterSettingKind.Url when !IsHttpUrl(value) => "Enter a full http or https address.",
                AdapterSettingKind.WholeNumber when !int.TryParse(
                    value, NumberStyles.None, CultureInfo.InvariantCulture, out _) => "Enter a whole number.",
                AdapterSettingKind.YesNo when value is not ("yes" or "no") => "Choose yes or no.",
                _ => null
            };
            if (problem is not null) problems.Add(new(field.Key, problem));
        }

        return problems;
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        string.IsNullOrEmpty(uri.UserInfo);
}
