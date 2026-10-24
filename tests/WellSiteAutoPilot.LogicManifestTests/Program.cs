using System.Text.Json;
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
LogicModuleManifestValidator.Validate(validManifest with { RequiredAdapterKey = "foresite" });
AssertFailure(validManifest with { RequiredAdapterKey = "invalid adapter" }, "MODULE_IDENTIFIER_INVALID");

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

// Optional description and settings schema.
LogicModuleManifestValidator.Validate(validManifest with { Description = "Reads pump fillage and proposes a speed." });
AssertFailure(validManifest with { Description = "  " }, "MODULE_DESCRIPTION_INVALID");
AssertFailure(
    validManifest with { Description = new string('x', LogicModuleManifestValidator.MaximumDescriptionLength + 1) },
    "MODULE_DESCRIPTION_INVALID");

await LogicModuleManifestValidator.ValidateParameterSchemaAsync(validManifest);
await LogicModuleManifestValidator.ValidateParameterSchemaAsync(validManifest with
{
    ParameterSchema = Schema("""{"type":"object","properties":{"targetFillage":{"type":"number","minimum":0,"maximum":100}},"required":["targetFillage"]}""")
});
await AssertSchemaFailure(validManifest with { ParameterSchema = Schema("[]") });
await AssertSchemaFailure(validManifest with { ParameterSchema = Schema("""{"type":"object","$ref":"http://example.invalid/x"}""") });

// Saved settings are checked against the schema; modules without one accept any object.
var withSchema = validManifest with
{
    ParameterSchema = Schema("""{"type":"object","properties":{"targetFillage":{"type":"number","minimum":0,"maximum":100}},"required":["targetFillage"],"additionalProperties":false}""")
};
await LogicModuleManifestValidator.ValidateParametersAsync(validManifest, """{"anything":true}""");
await LogicModuleManifestValidator.ValidateParametersAsync(withSchema, """{"targetFillage":75}""");
await AssertParametersFailure(withSchema, "{}");
await AssertParametersFailure(withSchema, """{"targetFillage":150}""");
await AssertParametersFailure(withSchema, """{"targetFillage":"high"}""");
await AssertParametersFailure(withSchema, """{"targetFillage":75,"extra":1}""");

await PackageChecks.RunAsync(validManifest);

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

static JsonElement Schema(string json)
{
    using var document = JsonDocument.Parse(json);
    return document.RootElement.Clone();
}

static async Task AssertSchemaFailure(LogicModuleManifest manifest)
{
    try
    {
        await LogicModuleManifestValidator.ValidateParameterSchemaAsync(manifest);
    }
    catch (WellSiteAutoPilotException exception) when (exception.Code == "MODULE_PARAMETER_SCHEMA_INVALID")
    {
        return;
    }

    throw new InvalidOperationException("Expected MODULE_PARAMETER_SCHEMA_INVALID.");
}

static async Task AssertParametersFailure(LogicModuleManifest manifest, string parametersJson)
{
    try
    {
        await LogicModuleManifestValidator.ValidateParametersAsync(manifest, parametersJson);
    }
    catch (WellSiteAutoPilotException exception) when (exception.Code == "CONFIGURED_LOGIC_PARAMETERS_INVALID")
    {
        return;
    }

    throw new InvalidOperationException($"Expected CONFIGURED_LOGIC_PARAMETERS_INVALID for {parametersJson}.");
}
