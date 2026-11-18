using System.Text.Json;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.ModuleSdk;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

/// <summary>
/// Turns one scalar current reading into the typed input a V2 module declared. The adapter decides which quantities it
/// can serve; the declared requirement and schema decide whether this reading is acceptable to the module.
/// </summary>
public static class ScalarStructuredInput
{
    public static bool IsScalar(ExecutionInputBindingV2 binding) =>
        AdapterReadRules.IsScalarCapability(binding.RequiredCapability);

    /// <summary>True when the adapter's read rule serves this exact scalar declaration for the binding's provider.</summary>
    public static bool IsServed(ExecutionInputBindingV2 binding) =>
        binding.Source is not null &&
        AdapterReadRules.Find(binding.ProviderId)?.AllowsCurrentRead(binding.Quantity, binding.RequiredCapability, binding.SchemaId) == true;

    public static async Task<StructuredInputValue> BuildAsync(ExecutionInputBindingV2 binding, StructuredInputDefinition declared,
        EngineeringValue measurement, DateTimeOffset evaluationTimeUtc, CancellationToken cancellationToken)
    {
        if (measurement.StructuredValue is not null || measurement.Value is not { } number)
            throw Failure("EXECUTION_INPUT_SHAPE_UNSUPPORTED");
        LogicModuleInputValidation.ValidateObservation(measurement.Quantity, measurement.Unit, measurement.TimestampUtc,
            measurement.Quality, declared.Requirement, evaluationTimeUtc);
        var data = JsonSerializer.SerializeToElement(number);
        await StructuredValueValidation.ValidateAsync(new(declared.SchemaId, data), declared.SchemaId, declared.Schema, cancellationToken);
        return new(binding.RequirementId, binding.AssetId, measurement.Quantity, new(declared.SchemaId, data),
            measurement.Unit, measurement.TimestampUtc, measurement.Quality, measurement.Source)
        { RetrievedAtUtc = measurement.RetrievedAtUtc, TimestampKind = measurement.TimestampKind };
    }

    private static WellSiteAutoPilotException Failure(string code) =>
        new(code, FailureKind.Validation, "Execution input did not satisfy its declared requirements.");
}
