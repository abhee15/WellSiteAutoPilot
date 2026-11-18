using System.Text.Json;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.Worker.DotNet.Execution;

namespace WellSiteAutoPilot.ExecutionContractV2Tests;

/// <summary>A V2 module that declares a scalar current reading is assignable and runs through the structured path.</summary>
internal static class ScalarRuntimeChecks
{
    private const string SchemaId = "authoring.fillage.number/v1";

    public static async Task RunAsync()
    {
        var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var foresite = AdapterReadRules.Find("foresite")!;
        Assert(foresite.AllowsCurrentRead("PumpFillage", AdapterReadRules.EngineeringCurrentCapability, SchemaId),
            "A registered scalar V2 declaration was refused assignment.");
        Assert(!foresite.AllowsCurrentRead("Quantity1", AdapterReadRules.EngineeringCurrentCapability, SchemaId),
            "An unregistered quantity was accepted for assignment.");
        Assert(!foresite.AllowsCurrentRead("PumpFillage", AdapterReadRules.EngineeringCurrentCapability, null),
            "A scalar V2 declaration without a schema identity was accepted.");
        Assert(!foresite.AllowsCurrentRead("MotorAmps", AdapterReadRules.EngineeringCurrentCapability, SchemaId),
            "A quantity the adapter does not serve was accepted.");
        Assert(!foresite.AllowsCurrentRead("PumpFillage", "other.current", SchemaId), "An unknown capability was accepted.");

        // A daily field is the provider's own name, read as the latest finished production day. Only ForeSite serves them,
        // only with the daily capability and a schema, and never a name that is not an identifier.
        var cygnet = AdapterReadRules.Find("cygnet")!;
        Assert(foresite.AllowsCurrentRead("CyclesYesterday", AdapterReadRules.EngineeringDailyCapability, SchemaId),
            "A provider daily field was refused assignment.");
        Assert(!foresite.AllowsCurrentRead("CyclesYesterday", AdapterReadRules.EngineeringCurrentCapability, SchemaId),
            "A daily field was accepted as a current reading.");
        Assert(!foresite.AllowsCurrentRead("CyclesYesterday", AdapterReadRules.EngineeringDailyCapability, null),
            "A daily declaration without a schema identity was accepted.");
        Assert(!foresite.AllowsCurrentRead("Cycles Yesterday", AdapterReadRules.EngineeringDailyCapability, SchemaId),
            "A non-identifier daily name was accepted.");
        Assert(!cygnet.AllowsCurrentRead("CyclesYesterday", AdapterReadRules.EngineeringDailyCapability, SchemaId),
            "An adapter with no daily record served a daily field.");
        Assert(foresite.AllowsScalarCurrentDeclaration("CyclesYesterday", AdapterReadRules.EngineeringDailyCapability, "number") &&
               !foresite.AllowsScalarCurrentDeclaration("CyclesYesterday", AdapterReadRules.EngineeringDailyCapability, "string"),
            "The daily declaration must be numeric.");
        Assert(AdapterReadRules.IsScalarCapability(AdapterReadRules.EngineeringDailyCapability) &&
               !AdapterReadRules.IsScalarCapability(AdapterReadRules.StructuredCurrentCapability),
            "The scalar capabilities are current and daily.");

        var schema = Json("{\"type\":\"number\",\"minimum\":0,\"maximum\":100}");
        var requirement = new LogicDataRequirement("fillage", "well", "PumpFillage", "Current", "%", 60, false,
            new(SchemaId, schema), AdapterReadRules.EngineeringCurrentCapability);
        Assert(GuidedInputSupport.ScalarDeclarationIsNumeric(requirement), "A numeric scalar declaration was refused.");
        Assert(!GuidedInputSupport.ScalarDeclarationIsNumeric(requirement with { ValueSchema = new(SchemaId, Json("{\"type\":\"string\"}")) }),
            "A text schema on a scalar current reading was accepted.");
        Assert(!GuidedInputSupport.ScalarDeclarationIsNumeric(requirement with { ValueSchema = null }),
            "A scalar current reading without a schema was accepted.");
        Assert(GuidedInputSupport.ScalarDeclarationIsNumeric(requirement with { RequiredCapability = null, ValueSchema = null }),
            "A V1 scalar was caught by the V2 rule.");

        var asset = Guid.NewGuid();
        var binding = new ExecutionInputBindingV2("fillage", asset, "foresite", "42", "PumpFillage", "Current", "%", 60, false, "{}",
            new(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), "foresite.well"), AdapterReadRules.EngineeringCurrentCapability, SchemaId);
        Assert(ScalarStructuredInput.IsScalar(binding) && ScalarStructuredInput.IsServed(binding), "A served scalar binding was refused.");
        Assert(!ScalarStructuredInput.IsServed(binding with { Source = null }), "A scalar binding without an authorized read plan was accepted.");
        Assert(!ScalarStructuredInput.IsServed(binding with { Quantity = "Quantity1" }), "A scalar binding for an unregistered quantity was accepted.");
        Assert(!ScalarStructuredInput.IsScalar(binding with { RequiredCapability = "structured.current" }), "Cards were treated as scalars.");

        var declared = new StructuredInputDefinition(requirement, SchemaId, schema);
        EngineeringValue Value(string quantity = "PumpFillage", decimal? value = 80, string unit = "%", string quality = "Good", DateTimeOffset? at = null) =>
            new(quantity, value, unit, at ?? now, quality, "foresite:surveillance");

        var input = await ScalarStructuredInput.BuildAsync(binding, declared, Value(), now, CancellationToken.None);
        Assert(input.Value.SchemaId == SchemaId && input.Value.Data.GetDecimal() == 80m && input.Unit == "%" &&
               input.RequirementId == "fillage" && input.AssetId == asset && input.Quality == "Good",
            "A scalar reading lost its value, unit, schema identity or provenance.");

        await RejectAsync(() => ScalarStructuredInput.BuildAsync(binding, declared, Value(unit: "kg"), now, CancellationToken.None), "EXECUTION_INPUT_QUANTITY_MISMATCH");
        await RejectAsync(() => ScalarStructuredInput.BuildAsync(binding, declared, Value(quantity: "SPM"), now, CancellationToken.None), "EXECUTION_INPUT_QUANTITY_MISMATCH");
        await RejectAsync(() => ScalarStructuredInput.BuildAsync(binding, declared, Value(quality: "Unknown"), now, CancellationToken.None), "EXECUTION_INPUT_QUALITY_REJECTED");
        await RejectAsync(() => ScalarStructuredInput.BuildAsync(binding, declared, Value(at: now.AddMinutes(-10)), now, CancellationToken.None), "EXECUTION_INPUT_STALE");
        await RejectAsync(() => ScalarStructuredInput.BuildAsync(binding, declared, Value(value: null), now, CancellationToken.None), "EXECUTION_INPUT_SHAPE_UNSUPPORTED");
        await RejectAsync(() => ScalarStructuredInput.BuildAsync(binding, declared,
            Value(value: null) with { StructuredValue = new("x/v1", Json("[]")) }, now, CancellationToken.None), "EXECUTION_INPUT_SHAPE_UNSUPPORTED");
        await RejectAsync(() => ScalarStructuredInput.BuildAsync(binding, declared, Value(value: 120), now, CancellationToken.None), "MODULE_VALUE_INVALID");

        Console.WriteLine("Scalar V2 runtime: assignment rules, numeric declaration, structured-path input and rejection reasons passed.");
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static async Task RejectAsync(Func<Task> operation, string code)
    {
        try { await operation(); }
        catch (WellSiteAutoPilotException error) when (error.Code == code) { return; }
        catch (WellSiteAutoPilotException error) { throw new InvalidOperationException($"Expected {code} but got {error.Code}: {error.Message}"); }
        throw new InvalidOperationException("Expected rejection: " + code);
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
