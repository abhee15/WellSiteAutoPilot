using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Application.Logic;

public static class DeclarativeThresholdPreview
{
    /// <summary>Evaluates the immutable artifact against a supplied sample without contacting a provider.</summary>
    public static async Task<DeclarativeThresholdEvidence> EvaluateAsync(
        DeclarativeThresholdArtifact artifact, DeclarativeThresholdSample sample,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(sample);
        cancellationToken.ThrowIfCancellationRequested();
        var module = artifact.Package.DeclarativeModule
            ?? throw new ArgumentException("The artifact requires a declarative evaluator.", nameof(artifact));
        var input = module.Input;
        var asset = new LogicAssetContext(input.AssetRole, sample.FixtureAssetId, sample.ParameterOverridesJson);
        var inputs = new List<LogicInputValue>(2);
        if (sample.Input is { } inputValue) inputs.Add(inputValue);
        if (sample.SecondInput is { } secondValue) inputs.Add(secondValue);
        var context = new LogicModuleExecutionContext(sample.RunId, module.Identity, sample.CorrelationId,
            sample.EvaluatedAtUtc, sample.ParametersJson, [asset],
            inputs);
        try
        {
            var result = await module.ExecuteAsync(context, cancellationToken);
            return new(artifact.Package.Sha256, sample, result, null);
        }
        catch (WellSiteAutoPilotException error) when (error.Kind == FailureKind.Validation)
        {
            return new(artifact.Package.Sha256, sample, null, error.Code);
        }
    }
}

public sealed record DeclarativeThresholdSample(
    Guid RunId, string CorrelationId, Guid FixtureAssetId, DateTimeOffset EvaluatedAtUtc,
    string ParametersJson, string ParameterOverridesJson, LogicInputValue? Input, LogicInputValue? SecondInput = null);

public sealed record DeclarativeThresholdEvidence(
    string PackageSha256, DeclarativeThresholdSample Sample, LogicModuleExecutionResult? Result, string? FailureCode);
