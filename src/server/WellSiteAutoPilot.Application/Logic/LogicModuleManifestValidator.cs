using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

public static partial class LogicModuleManifestValidator
{
    public static void Validate(LogicModuleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.AssetRequirements is null || manifest.DataRequirements is null ||
            manifest.CommandRequirements is null || manifest.AssetRequirements.Any(item => item is null) ||
            manifest.DataRequirements.Any(item => item is null) || manifest.CommandRequirements.Any(item => item is null))
        {
            Fail("MODULE_MANIFEST_INVALID", "Module requirements must be arrays without null entries.");
        }
        if (!Enum.IsDefined(manifest.Runtime) || !Enum.IsDefined(manifest.ExecutionProfile))
        {
            Fail("MODULE_MANIFEST_INVALID", "The module runtime or execution profile is unsupported.");
        }

        if (manifest.ManifestVersion != 1)
        {
            Fail(
                "MODULE_MANIFEST_VERSION_UNSUPPORTED",
                "Only Logic Module manifest version 1 is currently supported.");
        }

        RequireIdentifier(manifest.ModuleId, "Module ID");
        if (manifest.RequiredAdapterKey is { } adapterKey) RequireIdentifier(adapterKey, "Required adapter key");
        RequireText(manifest.DisplayName, "Display name");
        RequireText(manifest.Publisher, "Publisher");
        RequireText(manifest.EntryPoint, "Entry point");

        if (!LogicModuleSemanticVersion.TryParse(manifest.Version, out _))
        {
            Fail(
                "MODULE_VERSION_INVALID",
                "Logic Module version must use semantic versioning.");
        }

        if (!LogicModuleSemanticVersion.TryParse(manifest.MinimumSdkVersion, out _))
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
        ValidateDescription(manifest.Description);
    }

    /// <summary>Longest module description accepted; it is shown in lists and cards.</summary>
    public const int MaximumDescriptionLength = 500;

    /// <summary>
    /// Checks the optional settings schema with the same rules Asset Type schemas use, so one
    /// supported subset exists across the product. Absent or null means "no settings".
    /// </summary>
    public static async Task ValidateParameterSchemaAsync(
        LogicModuleManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.ParameterSchema is not { } schema || schema.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (schema.ValueKind != JsonValueKind.Object)
        {
            Fail("MODULE_PARAMETER_SCHEMA_INVALID", "The module settings schema must be a JSON object.");
        }

        try
        {
            await WellSiteAutoPilot.Application.Assets.AssetAttributeSchemaValidator.ValidateDefinitionAsync(
                schema.GetRawText(), cancellationToken);
        }
        catch (WellSiteAutoPilotException exception) when (exception.Kind == FailureKind.Validation)
        {
            throw new WellSiteAutoPilotException(
                "MODULE_PARAMETER_SCHEMA_INVALID",
                FailureKind.Validation,
                "The module settings schema is malformed or uses unsupported features.",
                innerException: exception);
        }
    }

    /// <summary>
    /// Checks saved settings against the module's schema. A module without a schema accepts any
    /// JSON object, so modules installed before schemas existed behave exactly as before.
    /// </summary>
    public static async Task ValidateParametersAsync(
        LogicModuleManifest manifest, string parametersJson, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.ParameterSchema is not { } schema ||
            schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        try
        {
            await WellSiteAutoPilot.Application.Assets.AssetAttributeSchemaValidator.ValidateValuesAsync(
                schema.GetRawText(), parametersJson, cancellationToken);
        }
        catch (WellSiteAutoPilotException exception) when (
            exception.Code == "ASSET_ATTRIBUTE_VALUES_INVALID")
        {
            throw new WellSiteAutoPilotException(
                "CONFIGURED_LOGIC_PARAMETERS_INVALID",
                FailureKind.Validation,
                "The settings do not match what this module accepts.",
                innerException: exception);
        }
        catch (WellSiteAutoPilotException exception) when (
            exception.Code == "ASSET_ATTRIBUTE_SCHEMA_INVALID")
        {
            throw new WellSiteAutoPilotException(
                "MODULE_PARAMETER_SCHEMA_INVALID",
                FailureKind.Validation,
                "The module settings schema is malformed or uses unsupported features.",
                innerException: exception);
        }
    }

    private static void ValidateDescription(string? description)
    {
        if (description is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(description) || description.Length > MaximumDescriptionLength)
        {
            Fail(
                "MODULE_DESCRIPTION_INVALID",
                $"A module description must be between 1 and {MaximumDescriptionLength} characters.");
        }
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

            if (requirement.RequiredTraits is null)
            {
                Fail("MODULE_MANIFEST_INVALID", "Asset requirement traits must be an array.");
            }
            foreach (var trait in requirement.RequiredTraits!)
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

    [DoesNotReturn]
    private static void Fail(string code, string detail) =>
        throw new WellSiteAutoPilotException(
            code,
            FailureKind.Validation,
            detail);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

}
