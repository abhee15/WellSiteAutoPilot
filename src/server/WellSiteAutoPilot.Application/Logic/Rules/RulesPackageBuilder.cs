using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Logic.Rules;

/// <summary>
/// Turns an authored contract and its rules into the immutable package that is reviewed, approved and run.
/// The package is a version 1 declarative module, so it reads scalar numbers only (current readings and, by capability,
/// the latest production-day value); anything else is refused
/// here, with a reason, instead of being packaged and failing at run time.
/// </summary>
public static class RulesPackageBuilder
{
    private const string CurrentCapability = "engineering.current";
    private const string DailyCapability = AdapterReadRules.EngineeringDailyCapability;
    private static readonly DateTimeOffset ZipEpoch = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<DeclarativeThresholdArtifact> BuildAsync(
        LogicModuleManifest manifest, RulesDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();

        if (manifest.DataRequirements.Count == 0)
            throw Invalid("GUIDED_RULES_INPUT_REQUIRED", "Add at least one input before submitting.");
        if (definition.Rules.Count == 0)
            throw Invalid("GUIDED_RULES_RULE_REQUIRED", "Add at least one rule before submitting.");
        foreach (var input in manifest.DataRequirements)
        {
            if (input.Access != "Current" || input.RequiredCapability is not (null or CurrentCapability or DailyCapability))
                throw Invalid("GUIDED_RULES_INPUT_UNSUPPORTED",
                    "Only current and daily values can run in a module today. Remove '" + input.Quantity + "' or use one of those.");
        }

        var issues = RulesEngine.Validate(definition, manifest.DataRequirements.Select(input => input.Quantity).ToArray());
        if (issues.Count != 0)
            throw Invalid(issues[0].Code, issues[0].Message);

        using var schema = JsonDocument.Parse(DeclarativeRulesModule.ParameterSchemaJson(definition));
        var executable = manifest with
        {
            ManifestVersion = 1,
            EntryPoint = RulesDefinition.EntryPoint,
            MinimumSdkVersion = "1.1.0",
            OutputRequirements = null,
            CommandRequirements = [],
            DataRequirements = manifest.DataRequirements
                .Select(input => input with
                {
                    ValueSchema = null,
                    RequiredCapability = input.RequiredCapability == DailyCapability ? DailyCapability : null
                }).ToArray(),
            ParameterSchema = schema.RootElement.Clone()
        };
        await LogicModuleManifestValidator.ValidateSchemasAsync(executable, cancellationToken);

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            manifestEntry.LastWriteTime = ZipEpoch;
            await using (var destination = manifestEntry.Open())
                await JsonSerializer.SerializeAsync(destination, executable, Options, cancellationToken);

            var payload = archive.CreateEntry(DeclarativeRulesModule.PayloadPath, CompressionLevel.Optimal);
            payload.LastWriteTime = ZipEpoch;
            await using (var destination = payload.Open())
            await using (var writer = new StreamWriter(destination))
                await writer.WriteAsync(definition.ToJson().AsMemory(), cancellationToken);
        }

        buffer.Position = 0;
        var inspected = await LogicModulePackageReader.ReadAsync(buffer, cancellationToken);
        return new DeclarativeThresholdArtifact(buffer.ToArray(), inspected);
    }

    private static WellSiteAutoPilotException Invalid(string code, string message) =>
        new(code, FailureKind.Validation, message);
}
