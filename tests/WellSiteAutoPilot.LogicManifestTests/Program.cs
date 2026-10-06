using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

var validManifest = new LogicModuleManifest(
    1,
    "weatherford.target-fillage",
    "1.0.0",
    "Target Fillage",
    "Weatherford",
    LogicRuntimeKind.Python,
    ExecutionProfile.BoundedIterative,
    "main:run",
    "1.0.0",
    [
        new LogicAssetRequirement(
            "well",
            1,
            1,
            "well",
            ["rod-lift"])
    ],
    [
        new LogicDataRequirement(
            "pump-fillage",
            "well",
            "PumpFillage",
            "Current",
            "%",
            60,
            false)
    ],
    [
        new LogicCommandRequirement(
            "set-speed",
            "well",
            "SetPumpingSpeed",
            "PumpingSpeed",
            "spm")
    ]);

LogicModuleManifestValidator.Validate(validManifest);

AssertFailure(
    validManifest with { Version = "one" },
    "MODULE_VERSION_INVALID");

AssertFailure(
    validManifest with
    {
        DataRequirements =
        [
            new LogicDataRequirement(
                "pump-fillage",
                "unknown-role",
                "PumpFillage",
                "Current",
                "%",
                60,
                false)
        ]
    },
    "MODULE_DATA_ROLE_UNKNOWN");

AssertFailure(
    validManifest with
    {
        AssetRequirements =
        [
            new LogicAssetRequirement("well", 2, 1, "well", [])
        ]
    },
    "MODULE_ASSET_CARDINALITY_INVALID");

Console.WriteLine("Logic Module manifest validation checks passed.");
return 0;

static void AssertFailure(
    LogicModuleManifest manifest,
    string expectedCode)
{
    try
    {
        LogicModuleManifestValidator.Validate(manifest);
    }
    catch (WellSiteAutoPilotException exception) when (exception.Code == expectedCode)
    {
        return;
    }

    throw new InvalidOperationException(
        $"Expected Logic Module validation failure '{expectedCode}'.");
}
