using System.Diagnostics.CodeAnalysis;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Logic;

// Expectations must come from the host's authorized execution/catalog record,
// never from the archive being checked. A match is not a trust/approval decision.
public sealed record LogicModulePackageExpectation(
    string ModuleId, string ModuleVersion, string PackageSha256,
    LogicRuntimeKind Runtime, string SdkVersion);

public static class LogicModulePackageCompatibility
{
    public static void Validate(InspectedLogicModulePackage package, LogicModulePackageExpectation expected)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(expected);
        ValidateIdentity(package, expected.ModuleId, expected.ModuleVersion, expected.PackageSha256);
        ValidateHost(package.Manifest, expected.Runtime, expected.SdkVersion);
    }

    public static void ValidateHost(LogicModuleManifest manifest, LogicRuntimeKind runtime, string sdkVersion)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.ManifestVersion is not (1 or 2))
            Fail("MODULE_CONTRACT_UNSUPPORTED", "Structured packages can be reviewed but this deployment does not yet execute them.");
        if (!Enum.IsDefined(runtime) || manifest.Runtime != runtime)
            Fail("MODULE_RUNTIME_INCOMPATIBLE", "The package requires a different module runtime.");
        if (!LogicModuleSemanticVersion.TryParse(sdkVersion, out var hostVersion))
            Fail("MODULE_HOST_SDK_VERSION_INVALID", "The host SDK version must use semantic versioning.");
        if (!LogicModuleSemanticVersion.TryParse(manifest.MinimumSdkVersion, out var minimumVersion))
            Fail("MODULE_SDK_VERSION_INVALID", "Minimum SDK version must use semantic versioning.");
        if (hostVersion!.CompareTo(minimumVersion) < 0)
            Fail("MODULE_SDK_INCOMPATIBLE", "The package requires a newer module SDK than the host supports.");
    }

    public static void ValidateCatalog(InspectedLogicModulePackage package, string moduleId, string moduleVersion,
        string packageSha256, string manifestJson)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidateIdentity(package, moduleId, moduleVersion, packageSha256);
        if (string.IsNullOrEmpty(package.ManifestJson) || !string.Equals(package.ManifestJson, manifestJson, StringComparison.Ordinal))
            Fail("MODULE_PACKAGE_MANIFEST_MISMATCH", "The package manifest differs from the reviewed catalog manifest.");
    }

    private static void ValidateIdentity(InspectedLogicModulePackage package, string moduleId, string moduleVersion, string packageSha256)
    {
        if (packageSha256 is not { Length: 64 } checksum || !checksum.All(char.IsAsciiHexDigit))
            Fail("MODULE_PACKAGE_EXPECTATION_INVALID", "The expected package SHA-256 must contain 64 hexadecimal characters.");
        if (!string.Equals(package.Sha256, packageSha256, StringComparison.OrdinalIgnoreCase))
            Fail("MODULE_PACKAGE_HASH_MISMATCH", "The package does not match the expected SHA-256.");
        if (!string.Equals(package.Manifest.ModuleId, moduleId, StringComparison.Ordinal) ||
            !string.Equals(package.Manifest.Version, moduleVersion, StringComparison.Ordinal))
            Fail("MODULE_PACKAGE_IDENTITY_MISMATCH", "The package does not match the expected module identity and version.");
    }

    [DoesNotReturn]
    private static void Fail(string code, string detail) =>
        throw new WellSiteAutoPilotException(code, FailureKind.Validation, detail);
}
