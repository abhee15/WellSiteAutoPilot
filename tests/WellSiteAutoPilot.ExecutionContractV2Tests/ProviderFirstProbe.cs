using System.Text.Json;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.ExecutionContractV2Tests;

/// <summary>THROWAWAY probe for the provider-first plan. Prints facts (PROBE lines) and never fails the job; not for merge.</summary>
internal static class ProviderFirstProbe
{
    private static LogicValueSchema Schema(string type) => new("probe." + type + "/v1", JsonDocument.Parse("{\"type\":\"" + type + "\"}").RootElement);

    private static LogicDataRequirement Input(string quantity, string access = "Current", string? capability = "engineering.current", string type = "number") =>
        new("input-1", "well", quantity, access, null, null, false, Schema(type), capability);

    private static void Say(string text) => Console.WriteLine("PROBE " + text);

    public static void Run()
    {
        // Probe 1 and 7: what the static read rules say per provider, and key casing.
        foreach (var key in new[] { "foresite", "cygnet", "ForeSite", "CYGNET", "simulator", "petrovisor" })
        {
            var rule = AdapterReadRules.Find(key);
            Say($"find key={key} -> {(rule is null ? "null" : rule.AdapterKey + " resource=" + rule.ResourceKind + " daily=" + rule.ServesDailyQuantities + " structured=" + (rule.StructuredCurrentReads?.Count ?? 0) + " current=[" + string.Join(",", rule.CurrentQuantities.OrderBy(x => x, StringComparer.Ordinal)) + "]")}");
        }
        Say("rule members: " + string.Join(",", typeof(AdapterReadRule).GetProperties().Select(property => property.Name)));

        // Probe 4 and 8: the support reason per provider for each kind of input an author can declare today.
        string? Reason(string? required, string adapter, LogicDataRequirement input) =>
            AdapterReadRules.Find(adapter) is { } rule ? GuidedInputSupport.UnsupportedReason(required, adapter, rule.ResourceKind, input) ?? "(supported)" : "(no rule)";
        var cases = new (string Name, LogicDataRequirement Input)[]
        {
            ("current PumpFillage number", Input("PumpFillage")),
            ("current CasingPressure number", Input("CasingPressure")),
            ("current PumpFillage text", Input("PumpFillage", type: "string")),
            ("history PumpFillage", Input("PumpFillage", "History")),
            ("daily CyclesYesterday number", Input("CyclesYesterday", capability: AdapterReadRules.EngineeringDailyCapability)),
            ("daily on wrong access History", Input("CyclesYesterday", "History", AdapterReadRules.EngineeringDailyCapability)),
            ("free-form unregistered quantity", Input("Quantity1")),
            ("no capability", Input("PumpFillage", capability: null)),
            ("cards structured", new LogicDataRequirement("cards", "well", ForeSiteSurfaceCard.Quantity, "Current", null, 60, false,
                new(ForeSiteSurfaceCard.SchemaId, JsonDocument.Parse(ForeSiteSurfaceCard.JsonSchema).RootElement), AdapterReadRules.StructuredCurrentCapability)),
            ("calibration record", new LogicDataRequirement("cal", "well", "Calibration", "Current", null, null, false,
                new("sample.calibration/v1", JsonDocument.Parse("{\"type\":\"object\"}").RootElement), AdapterReadRules.StructuredCurrentCapability))
        };
        foreach (var (name, input) in cases)
            foreach (var provider in new[] { "foresite", "cygnet" })
                Say($"reason case='{name}' primary={provider} on={provider}: {Reason(provider, provider, input)} | wrong-asset(on {(provider == "foresite" ? "cygnet" : "foresite")}): {Reason(provider, provider == "foresite" ? "cygnet" : "foresite", input)}");

        // Probe 6: does hint validation know about the primary provider?
        var hintManifest = new LogicModuleManifest(2, "authoring.probe", "1.0.0", "Probe", "Test", LogicRuntimeKind.DotNet,
            ExecutionProfile.ScheduledOneShot, GuidedContractDraft.UnsetEntryPoint, "1.2.0", [new("well", 1, 1, null, [])],
            [Input("PumpFillage")], [], RequiredAdapterKey: "foresite");
        foreach (var hint in new[]
        {
            new GuidedSourceHint("input-1", "cygnet", "no-such-operation", false),
            new GuidedSourceHint("input-1", "foresite", "no-such-operation", false),
            new GuidedSourceHint("input-1", null, "current.pump-fillage", true)
        })
            try { GuidedSourceHints.Validate(new GuidedModuleDraftContent(hintManifest, SourceHints: [hint])); Say($"hint adapter={hint.AdapterKey ?? "null"} op={hint.OperationKey}: accepted"); }
            catch (Exception exception) { Say($"hint adapter={hint.AdapterKey ?? "null"} op={hint.OperationKey}: rejected {exception.GetType().Name}"); }
    }
}
