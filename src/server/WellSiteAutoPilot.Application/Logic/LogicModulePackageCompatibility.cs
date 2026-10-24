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
        if (expected.PackageSha256 is not { Length: 64 } checksum || !checksum.All(char.IsAsciiHexDigit))
            Fail("MODULE_PACKAGE_EXPECTATION_INVALID", "The expected package SHA-256 must contain 64 hexadecimal characters.");
        if (!string.Equals(package.Sha256, expected.PackageSha256, StringComparison.OrdinalIgnoreCase))
            Fail("MODULE_PACKAGE_HASH_MISMATCH", "The package does not match the expected SHA-256.");
        if (!string.Equals(package.Manifest.ModuleId, expected.ModuleId, StringComparison.Ordinal) ||
            !string.Equals(package.Manifest.Version, expected.ModuleVersion, StringComparison.Ordinal))
            Fail("MODULE_PACKAGE_IDENTITY_MISMATCH", "The package does not match the expected module identity and version.");
        if (!Enum.IsDefined(expected.Runtime) || package.Manifest.Runtime != expected.Runtime)
            Fail("MODULE_RUNTIME_INCOMPATIBLE", "The package requires a different module runtime.");
        if (!LogicModuleSemanticVersion.TryParse(expected.SdkVersion, out var hostVersion))
            Fail("MODULE_HOST_SDK_VERSION_INVALID", "The host SDK version must use semantic versioning.");
        if (!LogicModuleSemanticVersion.TryParse(package.Manifest.MinimumSdkVersion, out var minimumVersion))
            Fail("MODULE_SDK_VERSION_INVALID", "Minimum SDK version must use semantic versioning.");
        if (hostVersion!.CompareTo(minimumVersion) < 0)
            Fail("MODULE_SDK_INCOMPATIBLE", "The package requires a newer module SDK than the host supports.");
    }

    [DoesNotReturn]
    private static void Fail(string code, string detail) =>
        throw new WellSiteAutoPilotException(code, FailureKind.Validation, detail);
}
