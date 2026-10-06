using System.Text.RegularExpressions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

public static partial class LogicModuleManifestValidator
{
    public static void Validate(LogicModuleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.ManifestVersion != 1)
        {
            Fail(
                "MODULE_MANIFEST_VERSION_UNSUPPORTED",
                "Only Logic Module manifest version 1 is currently supported.");
        }

        RequireIdentifier(manifest.ModuleId, "Module ID");
        RequireText(manifest.DisplayName, "Display name");
        RequireText(manifest.Publisher, "Publisher");
        RequireText(manifest.EntryPoint, "Entry point");

        if (!SemanticVersion().IsMatch(manifest.Version))
        {
            Fail(
                "MODULE_VERSION_INVALID",
                "Logic Module version must use semantic versioning.");
        }

        if (!SemanticVersion().IsMatch(manifest.MinimumSdkVersion))
        {
            Fail(
                "MODULE_SDK_VERSION_INVALID",
                "Minimum SDK version must use semantic versioning.");
        }

        if (manifest.AssetRequirements.Count == 0)
        {
            Fail(
                "MODULE_ASSET_REQUIREMENTS_EMPTY",
                "A Logic Module must declare at least one asset requirement.");
        }

        ValidateAssetRequirements(manifest.AssetRequirements);
        ValidateDataRequirements(manifest.AssetRequirements, manifest.DataRequirements);
        ValidateCommandRequirements(manifest.AssetRequirements, manifest.CommandRequirements);
    }

    private static void ValidateAssetRequirements(
        IReadOnlyCollection<LogicAssetRequirement> requirements)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var requirement in requirements)
        {
            RequireIdentifier(requirement.Role, "Asset role");

            if (!roles.Add(requirement.Role))
            {
                Fail(
                    "MODULE_ASSET_ROLE_DUPLICATE",
                    $"Asset role '{requirement.Role}' is declared more than once.");
            }

            if (requirement.MinimumCount < 0 ||
                requirement.MaximumCount < 1 ||
                requirement.MaximumCount < requirement.MinimumCount)
            {
                Fail(
                    "MODULE_ASSET_CARDINALITY_INVALID",
                    $"Asset role '{requirement.Role}' has invalid cardinality.");
            }

            foreach (var trait in requirement.RequiredTraits)
            {
                RequireIdentifier(trait, $"Trait for role '{requirement.Role}'");
            }
        }
    }

    private static void ValidateDataRequirements(
        IReadOnlyCollection<LogicAssetRequirement> assetRequirements,
        IReadOnlyCollection<LogicDataRequirement> requirements)
    {
        var roles = assetRequirements
            .Select(item => item.Role)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var requirement in requirements)
        {
            RequireIdentifier(requirement.Id, "Data requirement ID");
            RequireIdentifier(requirement.AssetRole, "Data requirement asset role");
            RequireIdentifier(requirement.Quantity, "Quantity");

            if (!ids.Add(requirement.Id))
            {
                Fail(
                    "MODULE_DATA_REQUIREMENT_DUPLICATE",
                    $"Data requirement '{requirement.Id}' is declared more than once.");
            }

            if (!roles.Contains(requirement.AssetRole))
            {
                Fail(
                    "MODULE_DATA_ROLE_UNKNOWN",
                    $"Data requirement '{requirement.Id}' references unknown asset role '{requirement.AssetRole}'.");
            }

            if (requirement.Access is not ("Current" or "History"))
            {
                Fail(
                    "MODULE_DATA_ACCESS_INVALID",
                    $"Data requirement '{requirement.Id}' must use Current or History access.");
            }

            if (requirement.MaximumAgeSeconds is <= 0)
            {
                Fail(
                    "MODULE_DATA_MAX_AGE_INVALID",
                    $"Data requirement '{requirement.Id}' maximum age must be greater than zero.");
            }
        }
    }

    private static void ValidateCommandRequirements(
        IReadOnlyCollection<LogicAssetRequirement> assetRequirements,
        IReadOnlyCollection<LogicCommandRequirement> requirements)
    {
        var roles = assetRequirements
            .Select(item => item.Role)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var requirement in requirements)
        {
            RequireIdentifier(requirement.Id, "Command requirement ID");
            RequireIdentifier(requirement.AssetRole, "Command requirement asset role");
            RequireIdentifier(requirement.Command, "Command");

            if (!ids.Add(requirement.Id))
            {
                Fail(
                    "MODULE_COMMAND_REQUIREMENT_DUPLICATE",
                    $"Command requirement '{requirement.Id}' is declared more than once.");
            }

            if (!roles.Contains(requirement.AssetRole))
            {
                Fail(
                    "MODULE_COMMAND_ROLE_UNKNOWN",
                    $"Command requirement '{requirement.Id}' references unknown asset role '{requirement.AssetRole}'.");
            }
        }
    }

    private static void RequireIdentifier(string value, string name)
    {
        RequireText(value, name);

        if (!Identifier().IsMatch(value))
        {
            Fail(
                "MODULE_IDENTIFIER_INVALID",
                $"{name} contains unsupported characters.");
        }
    }

    private static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Fail(
                FailureCodes.ValidationFailed,
                $"{name} is required.");
        }
    }

    private static void Fail(string code, string detail) =>
        throw new WellSiteAutoPilotException(
            code,
            FailureKind.Validation,
            detail);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

    [GeneratedRegex(
        @"^(0|[1-9]d*).(0|[1-9]d*).(0|[1-9]d*)(?:-[0-9A-Za-z.-]+)?(?:+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersion();
}
