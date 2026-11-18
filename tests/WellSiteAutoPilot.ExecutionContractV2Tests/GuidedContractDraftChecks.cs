using System.Text.Json;
using System.Text.Json.Serialization;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.ExecutionContractV2Tests;

/// <summary>Contract-only guided drafts: typed, versioned, valid without an Asset, and never executable.</summary>
internal static class GuidedContractDraftChecks
{
    // Same options the draft repository uses to persist content.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    // Mirrors the structured-cards sample: a collection input, a record input, an array output and a scalar output.
    private static LogicModuleManifest Contract() => new(2, "authoring.card-calibration", "1.0.0", "Card calibration", "Test",
        LogicRuntimeKind.DotNet, ExecutionProfile.ScheduledOneShot, GuidedContractDraft.UnsetEntryPoint, "1.2.0",
        [new("well", 1, 1, null, [])],
        [
            new("cards", "well", "SurfaceCards", "Current", null, 60, false,
                new("foresite.surface-cards/v1", Json("""{"type":"array","items":{"type":"object","properties":{"id":{"type":"string","minLength":1,"maxLength":128},"load":{"type":"array","items":{"type":"number","minimum":-1000000,"maximum":1000000},"minItems":2,"maxItems":1024}},"required":["id","load"],"additionalProperties":false},"minItems":1,"maxItems":16}""")),
                "structured.current"),
            new("calibration", "well", "Calibration", "Current", null, null, false,
                new("sample.calibration/v1", Json("""{"type":"object","properties":{"loadScale":{"type":"number","minimum":0.1,"maximum":10},"loadOffset":{"type":"number","minimum":-10000,"maximum":10000}},"required":["loadScale","loadOffset"],"additionalProperties":false}""")),
                "structured.current")
        ],
        [],
        Description: "Declared contract only.",
        ParameterSchema: Json("""{"type":"object","properties":{"loadScale":{"type":"number","minimum":0.1,"maximum":10}},"additionalProperties":false}"""),
        OutputRequirements:
        [
            new("calibrated-cards", "CalibratedCards", null, new("sample.calibrated-cards/v2",
                Json("""{"type":"array","items":{"type":"object","properties":{"id":{"type":"string","minLength":1,"maxLength":128}},"required":["id"],"additionalProperties":false},"minItems":1,"maxItems":16}"""))),
            new("peak-load", "PeakLoad", "lb", new("sample.peak-load/v2", Json("""{"type":"number"}""")))
        ]);

    public static async Task RunAsync()
    {
        var manifest = Contract();

        // A structured contract with arrays, a record, two output shapes and a parameter schema is valid with no Asset or Connection.
        await GuidedContractDraft.ValidateAsync(manifest);

        // A blank draft (one neutral role, no ports yet) can be saved, but the same manifest is not an executable module.
        var blank = manifest with { DataRequirements = [], OutputRequirements = [], ParameterSchema = null };
        await GuidedContractDraft.ValidateAsync(blank);
        await RejectAsync(() => LogicModuleManifestValidator.ValidateSchemasAsync(blank), "MODULE_CONTRACT_INVALID");
        await RejectAsync(() => GuidedContractDraft.ValidateAsync(blank with { OutputRequirements = null }), "MODULE_CONTRACT_INVALID");
        await RejectAsync(() => GuidedContractDraft.ValidateAsync(blank with { AssetRequirements = [] }), "MODULE_ASSET_REQUIREMENTS_EMPTY");

        // The save route reads the posted manifest text: a blank or one-sided draft must read as a draft yet never as an executable module.
        foreach (var partial in new[] { blank, blank with { DataRequirements = manifest.DataRequirements } })
        {
            var text = JsonSerializer.Serialize(partial, Options);
            using (var draftStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)))
                Assert((await LogicModuleManifestReader.ReadDraftAsync(draftStream)).EntryPoint == GuidedContractDraft.UnsetEntryPoint,
                    "A blank contract-only manifest could not be read as a draft.");
            await RejectAsync(async () =>
            {
                using var strict = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
                await LogicModuleManifestReader.ReadAsync(strict);
            }, "MODULE_CONTRACT_INVALID");
        }

        // Persisted content round-trips unchanged and is still contract-only; threshold content from before this change still reads.
        var content = new GuidedModuleDraftContent(manifest);
        var restored = JsonSerializer.Deserialize<GuidedModuleDraftContent>(JsonSerializer.Serialize(content, Options), Options)!;
        Assert(restored.IsContractOnly && restored.Manifest.DataRequirements.Count == 2 &&
            restored.Manifest.OutputRequirements!.Count == 2 && restored.Manifest.ParameterSchema is not null,
            "A contract-only draft did not round-trip.");
        await GuidedContractDraft.ValidateAsync(restored.Manifest);
        var legacy = JsonSerializer.Deserialize<GuidedModuleDraftContent>(
            JsonSerializer.Serialize(new { manifest, definition = new ThresholdDefinition("threshold", 70, "LOW", "Low", "Review.") }, Options), Options)!;
        Assert(!legacy.IsContractOnly && legacy.Definition!.Default == 70, "Existing threshold draft content no longer reads.");

        // Server-owned placeholder, version 2 and no commands are required.
        await RejectAsync(() => GuidedContractDraft.ValidateAsync(manifest with { EntryPoint = "Real.Module" }), "GUIDED_CONTRACT_INVALID");
        await RejectAsync(() => GuidedContractDraft.ValidateAsync(manifest with { ManifestVersion = 1 }), "GUIDED_CONTRACT_INVALID");
        await RejectAsync(() => GuidedContractDraft.ValidateAsync(manifest with
        {
            CommandRequirements = [new("stop", "well", "Stop", null, null)]
        }), "GUIDED_CONTRACT_COMMANDS_UNSUPPORTED");

        // The existing manifest rules still apply: duplicate ids, missing schema, malformed schema and unknown asset roles.
        await RejectAsync(() => GuidedContractDraft.ValidateAsync(manifest with
        {
            DataRequirements = [.. manifest.DataRequirements, manifest.DataRequirements.First()]
        }), "MODULE_DATA_REQUIREMENT_DUPLICATE");
        await RejectAsync(() => GuidedContractDraft.ValidateAsync(manifest with
        {
            DataRequirements = [manifest.DataRequirements.First() with { ValueSchema = null }]
        }), "MODULE_CONTRACT_INVALID");
        await RejectAsync(() => GuidedContractDraft.ValidateAsync(manifest with
        {
            OutputRequirements = [new("peak-load", "PeakLoad", "lb", new("sample.peak-load/v2", Json("""{"minimum":1}""")))]
        }), "MODULE_VALUE_SCHEMA_INVALID");
        await RejectAsync(() => GuidedContractDraft.ValidateAsync(manifest with
        {
            DataRequirements = [manifest.DataRequirements.First() with { AssetRole = "missing-role" }]
        }), "MODULE_DATA_ROLE_UNKNOWN");
        await RejectAsync(() => GuidedContractDraft.ValidateAsync(manifest with
        {
            ParameterSchema = Json("\"settings\"")
        }), "MODULE_PARAMETER_SCHEMA_INVALID");

        // Revision fencing and identity apply to contract drafts exactly as to threshold drafts.
        var draft = new GuidedModuleDraft(Guid.NewGuid(), Guid.NewGuid(), 1, content);
        var renamed = draft.Edit(1, new(manifest with { DisplayName = "Card calibration v2" }));
        Assert(renamed.Revision == 2 && renamed.Content.IsContractOnly, "A contract edit did not advance the revision.");
        await RejectAsync(() => Task.FromResult(draft.Edit(2, content)), "GUIDED_DRAFT_REVISION_CONFLICT");
        await RejectAsync(() => Task.FromResult(draft.Edit(1, new(manifest with { ModuleId = "authoring.other" }))),
            "GUIDED_DRAFT_IDENTITY_IMMUTABLE");
        await RejectAsync(() => Task.FromResult(draft.Edit(1, new(manifest, new ThresholdDefinition("t", 1, "C", "T", "D")))),
            "GUIDED_DRAFT_KIND_IMMUTABLE");

        // No logic exists, so preparing a submission is an explicit unavailable state, never a passing test.
        await RejectAsync(() => draft.PrepareSubmissionAsync(1, []), "GUIDED_DRAFT_LOGIC_UNSUPPORTED");

        // A V2 scalar declaration is recognised for authoring-time checks only; assignment rules stay strict until the worker runs it.
        var foresite = AdapterReadRules.Find("foresite")!;
        var cygnet = AdapterReadRules.Find("cygnet")!;
        Assert(foresite.AllowsScalarCurrentDeclaration("PumpFillage", "engineering.current", "number") &&
            cygnet.AllowsScalarCurrentDeclaration("CasingPressure", "engineering.current", "number"),
            "A registered scalar current quantity was not recognised for authoring.");
        Assert(!foresite.AllowsScalarCurrentDeclaration("CasingPressure", "engineering.current", "number") &&
            !foresite.AllowsScalarCurrentDeclaration("PumpFillage", "structured.current", "number") &&
            !foresite.AllowsScalarCurrentDeclaration("PumpFillage", "engineering.current", "string") &&
            !foresite.AllowsScalarCurrentDeclaration("SurfaceCards", "engineering.current", "number") &&
            !foresite.AllowsScalarCurrentDeclaration(null, "engineering.current", "number"),
            "An unregistered, wrong-capability, wrong-type or structured quantity was accepted for authoring.");
        Assert(foresite.AllowsCurrentRead("PumpFillage", "engineering.current", "authoring.input-1.number/v1"),
            "Assignment-time read rules must accept a registered V2 scalar declaration the worker can run.");
        Assert(!foresite.AllowsCurrentRead("Quantity1", "engineering.current", "authoring.input-1.number/v1"),
            "Assignment-time read rules accepted an unregistered V2 scalar quantity.");

        // Unsupported inputs say why, so the author knows whether to change the quantity, the access mode or the type.
        LogicDataRequirement Input(string quantity, string access = "Current", string? capability = "engineering.current", string type = "number") =>
            new("input-1", "well", quantity, access, null, null, false,
                new("authoring.input-1.test/v1", Json("{\"type\":\"" + type + "\"}")), capability);
        string? Reason(LogicDataRequirement input, string adapter = "foresite") =>
            GuidedInputSupport.UnsupportedReason(null, adapter, AdapterReadRules.Find(adapter)!.ResourceKind, input);
        Assert(Reason(Input("PumpFillage")) is null, "A registered scalar input was reported unsupported.");
        Assert(Reason(Input("Quantity1")) == GuidedInputSupport.QuantityUnregistered, "An unregistered quantity was not named.");
        Assert(Reason(Input("PumpFillage", "History")) == GuidedInputSupport.AccessUnsupported, "A non-current access mode was not named.");
        Assert(Reason(Input("PumpFillage", capability: "other.current")) == GuidedInputSupport.CapabilityUnsupported, "A wrong capability was not named.");
        Assert(Reason(Input("PumpFillage", capability: AdapterReadRules.StructuredCurrentCapability)) == GuidedInputSupport.QuantityUnregistered,
            "A scalar quantity declared as a structured input was accepted.");
        Assert(Reason(Input("PumpFillage", type: "string")) == GuidedInputSupport.ValueTypeUnsupported, "A wrong value type was not named.");
        Assert(GuidedInputSupport.IsScalarSeries(Input("PumpFillage")), "A numeric input declared with a value schema must still preview as a history series.");
        Assert(!GuidedInputSupport.IsScalarSeries(Input("PumpFillage", type: "string")), "A text input was offered a numeric history preview.");
        var cards = new LogicDataRequirement("cards", "well", ForeSiteSurfaceCard.Quantity, "Current", null, 60, false,
            new(ForeSiteSurfaceCard.SchemaId, Json(ForeSiteSurfaceCard.JsonSchema)), AdapterReadRules.StructuredCurrentCapability);
        Assert(Reason(cards) is null, "A structured card input the provider registers was reported unsupported.");
        Assert(!GuidedInputSupport.IsScalarSeries(cards), "A structured card input was offered a numeric history preview.");
        Assert(Reason(cards with { ValueSchema = new("authoring.cards/v1", cards.ValueSchema!.Schema) }) == GuidedInputSupport.QuantityUnregistered,
            "A structured input with a schema id the provider does not register was accepted.");
        Assert(Reason(cards, "cygnet") == GuidedInputSupport.QuantityUnregistered, "Cards were accepted on a connection that does not serve them.");
        Assert(GuidedInputSupport.UnsupportedReason("cygnet", "foresite", AdapterReadRules.Find("foresite")!.ResourceKind, Input("PumpFillage")) == GuidedInputSupport.AdapterMismatch,
            "A module pinned to another adapter was not named.");

        // Source hints sit beside the contract: validated against the declared inputs, a preference unless required.
        GuidedModuleDraftContent WithHints(params GuidedSourceHint[] hints) => new(manifest, SourceHints: hints);
        GuidedSourceHints.Validate(WithHints());
        GuidedSourceHints.Validate(WithHints(new GuidedSourceHint("cards", "foresite", "surface-cards", true),
            new GuidedSourceHint("calibration", null, "calibration", false, "{\"scale\":2}")));
        foreach (var bad in new[]
        {
            WithHints(new GuidedSourceHint("missing", "foresite", null, false)),
            WithHints(new GuidedSourceHint("cards", null, null, false)),
            WithHints(new GuidedSourceHint("cards", "foresite", null, false), new GuidedSourceHint("cards", "cygnet", null, false)),
            WithHints(new GuidedSourceHint("cards", "bad key!", null, false)),
            WithHints(new GuidedSourceHint("cards", "foresite", null, false, "{}")),
            WithHints(new GuidedSourceHint("cards", "foresite", "op", false, "[1]")),
            WithHints(new GuidedSourceHint("cards", "foresite", "op", false, "{not json")),
            WithHints(new GuidedSourceHint("cards", "foresite", "op", false, "{\"v\":\"" + new string('x', 5000) + "\"}"))
        })
            Assert(Throws(() => GuidedSourceHints.Validate(bad), "GUIDED_SOURCE_HINT_INVALID"), "An invalid source hint was accepted.");
        Assert(Throws(() => GuidedSourceHints.Validate(WithHints(Enumerable.Range(0, 65)
            .Select(i => new GuidedSourceHint("cards", "foresite", null, false)).ToArray())), "GUIDED_SOURCE_HINT_INVALID"),
            "More source hints than the bound were accepted.");
        string[] operations = ["surface-cards", "history.pump-fillage"];
        Assert(GuidedSourceHints.UnmetReason(null, "foresite", operations) is null, "No hint blocked an input.");
        Assert(GuidedSourceHints.UnmetReason(new("cards", "cygnet", "nope", false), "foresite", operations) is null,
            "A preference blocked an input.");
        Assert(GuidedSourceHints.UnmetReason(new("cards", "foresite", "surface-cards", true), "foresite", operations) is null,
            "A satisfied required hint blocked an input.");
        Assert(GuidedSourceHints.UnmetReason(new("cards", "cygnet", null, true), "foresite", operations) == GuidedInputSupport.AdapterMismatch,
            "A required provider on another connection was not named.");
        Assert(GuidedSourceHints.UnmetReason(new("cards", null, "nope", true), "foresite", operations) == GuidedSourceHints.OperationUnavailable,
            "A required operation the connection lacks was not named.");

        Console.WriteLine("Contract-only guided drafts passed typed round trip, validation, fencing and unsupported-execution checks.");
    }

    private static async Task RejectAsync(Func<Task> operation, string code)
    {
        try { await operation(); }
        catch (WellSiteAutoPilotException error) when (error.Code == code) { return; }
        catch (WellSiteAutoPilotException error) { throw new InvalidOperationException($"Expected {code} but got {error.Code}: {error.Message}"); }
        throw new InvalidOperationException("Expected rejection: " + code);
    }

    private static bool Throws(Action action, string code)
    {
        try { action(); }
        catch (WellSiteAutoPilotException error) when (error.Code == code) { return true; }
        return false;
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
