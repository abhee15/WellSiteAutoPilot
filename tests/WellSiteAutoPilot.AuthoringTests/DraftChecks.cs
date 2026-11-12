using WellSiteAutoPilot.Application.Authoring;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.AuthoringTests;

internal static class DraftChecks
{
    private static ModuleDraft Draft(
        IEnumerable<DraftInput>? inputs = null, IEnumerable<DraftParameter>? parameters = null,
        IEnumerable<DraftStep>? steps = null, IEnumerable<DraftOutput>? outputs = null) =>
        new("Test", null, (inputs ?? []).ToList(), (parameters ?? []).ToList(), (steps ?? []).ToList(), (outputs ?? []).ToList());

    private static DraftInput In(string name, string source, bool required = true) => new(name, [new InputSource(source)], required);

    private static bool Reports(ModuleDraft draft, string node, string text) =>
        DraftAnalyzer.Analyze(draft).Problems.Any(p => p.Severity == "Error" && p.Node == node && p.Message.Contains(text, StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync()
    {
        foreach (var starter in AuthoringStarters.All)
        {
            var analysis = DraftAnalyzer.Analyze(starter.Draft);
            Check.That(analysis.Valid, $"starter '{starter.Id}' must be valid: " + string.Join("; ", analysis.Problems.Select(p => p.Node + ": " + p.Message)));
            Check.That(!analysis.Problems.Any(p => p.Severity == "Warning"), $"starter '{starter.Id}' must be warning-free as a teaching example");
        }
        Check.That(AuthoringStarters.All.Select(s => s.Id).Distinct().Count() == AuthoringStarters.All.Count, "starter ids are unique");

        Analyzer();
        await EvaluationAsync();
        await ServiceAsync();
    }

    private static void Analyzer()
    {
        var ok = Draft([In("A", "well:X")], [], [new DraftStep("S", "A + 1")], [new DraftOutput("O", "S")]);
        Check.That(DraftAnalyzer.Analyze(ok).Valid, "a simple chain is valid");
        Check.That(DraftAnalyzer.Analyze(ok).Order.SequenceEqual(["S"]), "steps are ordered");

        Check.That(Reports(Draft([In("A", "well:X")], [], [], [new DraftOutput("O", "B")]), "O", "'B' is not"), "unknown reference is reported at the output");
        Check.That(Reports(Draft([In("A", "well:X")], [], [new DraftStep("S", "A +")], [new DraftOutput("O", "S")]), "S", "ends too soon"), "parse error is reported at the step");
        Check.That(Reports(Draft([In("A", "well:X"), In("A", "well:Y")], [], [], [new DraftOutput("O", "A")]), "A", "already used"), "duplicate names");
        Check.That(Reports(Draft([In("1bad", "well:X")], [], [], [new DraftOutput("O", "1")]), "1bad", "start with a letter"), "bad identifier");
        Check.That(Reports(Draft([In("if", "well:X")], [], [], [new DraftOutput("O", "1")]), "if", "reserved"), "function names are reserved");
        Check.That(Reports(Draft([], [], [], []), "module", "at least one output"), "an output is required");
        Check.That(Reports(Draft([In("A", "well:X")], [], [], [new DraftOutput("O1", "A"), new DraftOutput("O2", "O1")]), "O2", "outputs cannot feed"), "outputs cannot be used as inputs");
        Check.That(Reports(Draft([new DraftInput("A", [])], [], [], [new DraftOutput("O", "A")]), "A", "where this input"), "an input needs a source");
        Check.That(Reports(Draft([new DraftInput("A", [new InputSource("well:X", "Bogus")])], [], [], [new DraftOutput("O", "A")]), "A", "unknown basis"), "basis is validated");
        Check.That(Reports(Draft([In("A", "well:X")], [], [], [new DraftOutput("O", "A", "setpoint")]), "O", "Name the set point"), "a set point output needs a target");

        var cycle = Draft([In("A", "well:X")], [], [new DraftStep("P", "Q + A"), new DraftStep("Q", "P")], [new DraftOutput("O", "Q")]);
        Check.That(Reports(cycle, "P", "depends on itself") && Reports(cycle, "Q", "depends on itself"), "a cycle is reported on every step in it");
        Check.That(Reports(Draft([], [], [new DraftStep("S", "S + 1")], [new DraftOutput("O", "S")]), "S", "cannot use itself"), "self reference");

        var unused = DraftAnalyzer.Analyze(Draft([In("A", "well:X"), In("B", "well:Y")], [], [], [new DraftOutput("O", "A")]));
        Check.That(unused.Valid && unused.Problems.Any(p => p.Severity == "Warning" && p.Node == "B"), "an unused input is a warning, not an error");

        var graph = DraftAnalyzer.Analyze(Draft([In("A", "well:X")], [], [new DraftStep("S", "A * 2")], [new DraftOutput("O", "S + A")])).Graph.ToDictionary(g => g.Name);
        Check.That(graph["A"].UsedBy.SequenceEqual(["O", "S"]), "tracing downstream from an input finds its consumers");
        Check.That(graph["O"].DependsOn.OrderBy(x => x).SequenceEqual(["A", "S"]), "tracing upstream from an output finds its sources");

        var one = Draft([In("A", "well:X")], [], [], [new DraftOutput("O", "A")]);
        var changed = Draft([In("A", "well:X")], [new DraftParameter("P", "1")], [], [new DraftOutput("O", "A")]);
        Check.That(DraftAnalyzer.Fingerprint(one) == DraftAnalyzer.Fingerprint(Draft([In("A", "well:X")], [], [], [new DraftOutput("O", "A")])), "the fingerprint is stable");
        Check.That(DraftAnalyzer.Fingerprint(one) != DraftAnalyzer.Fingerprint(changed), "any edit changes the fingerprint, so older test evidence goes stale");
    }

    private static InputTrace Trace(string name, string resolution, Val value, bool required = true, params string[] reasons) =>
        new(name, resolution, value, null, null, null, null, reasons, [], required);

    private static async Task EvaluationAsync()
    {
        var draft = AuthoringStarters.All.Single(s => s.Id == "threshold").Draft;
        var run = DraftEvaluator.Evaluate(draft, [Trace("Casing", "Resolved", Val.Of(300))]);
        Check.That(run.Outcome == ModuleOutcome.Runs, "resolved inputs run");
        Check.That(run.Steps.Single(s => s.Name == "IsHigh").Value.Flag == true, "step value");
        Check.That(run.Outputs.Single(o => o.Name == "Status_out").Value.Text == "High", "output follows the steps");
        Check.That(run.Steps.Single(s => s.Name == "Margin").DependsOn.OrderBy(x => x).SequenceEqual(["Casing", "HighLimit"]), "each step records what it read");

        var lowered = DraftEvaluator.Evaluate(draft, [Trace("Casing", "Resolved", Val.Of(300))], new Dictionary<string, string> { ["HighLimit"] = "400" });
        Check.That(lowered.Outputs.Single(o => o.Name == "Status_out").Value.Text == "Normal", "a parameter override changes the result");
        Check.That(lowered.Fingerprint == run.Fingerprint, "overriding a parameter for a test does not change the draft");

        var zero = DraftEvaluator.Evaluate(draft, [Trace("Casing", "Resolved", Val.Of(0))]);
        Check.That(zero.Outputs.Single(o => o.Name == "Margin_out").Value.Number == 250, "a real zero input is a valid value");

        var skipped = DraftEvaluator.Evaluate(draft, [Trace("Casing", "NotApplicable", Val.Missing("no casing"), true, "no casing")]);
        Check.That(skipped.Outcome == ModuleOutcome.Skipped && skipped.Outputs.All(o => !o.Value.Available), "a missing required input skips; outputs stay unavailable, not zero");
        var setup = DraftEvaluator.Evaluate(draft, [Trace("Casing", "NeedsSelection", Val.Missing("pick"))]);
        Check.That(setup.Outcome == ModuleOutcome.NeedsSetup, "needs selection asks the author to choose");
        var down = DraftEvaluator.Evaluate(draft, [Trace("Casing", "CannotDetermine", Val.Missing("offline"))]);
        Check.That(down.Outcome == ModuleOutcome.Unavailable, "an unreachable provider is not the same as not applicable");

        var optional = AuthoringStarters.All.Single(s => s.Id == "snapshot").Draft;
        var withoutFillage = DraftEvaluator.Evaluate(optional,
            [Trace("Casing", "Resolved", Val.Of(1)), Trace("Tubing", "Resolved", Val.Of(2)), Trace("Fillage", "NotApplicable", Val.Missing("none"), false)]);
        Check.That(withoutFillage.Outcome == ModuleOutcome.Runs && withoutFillage.Outputs.Single(o => o.Name == "Fillage_out").Value.Number == -1,
            "an optional input that is missing does not stop the module, and coalesce supplies the default");

        var broken = Draft([In("A", "well:X")], [], [], [new DraftOutput("O", "Nope")]);
        Check.That(DraftEvaluator.Evaluate(broken, []).Outcome == ModuleOutcome.Invalid, "an invalid draft is never evaluated");
        await Task.CompletedTask;
    }

    private static async Task ServiceAsync()
    {
        var fallback = AuthoringStarters.All.Single(s => s.Id == "fallback").Draft;

        var live = new FakeInputs(new() { ["surveillance:CasingPressure"] = FakeInputs.Value("surveillance:CasingPressure", 120) });
        var first = await AuthoringService.EvaluateAsync(fallback, live, "1", null, null);
        Check.That(first.Outcome == ModuleOutcome.Runs && first.Outputs[0].Value.Number == 120, "the first source that answers wins");
        Check.That(!live.Asked.Contains("welltest:AverageCasingPressure"), "later sources are not asked once one answered");
        Check.That(first.Inputs[0].UsedInput == "surveillance:CasingPressure" && first.Inputs[0].Unit == "psi", "the trace keeps source and unit");

        var wellTestOnly = new FakeInputs(new() { ["welltest:AverageCasingPressure"] = FakeInputs.Value("welltest:AverageCasingPressure", 15, ResolveBasis.TestSample) });
        var second = await AuthoringService.EvaluateAsync(fallback, wellTestOnly, "1", null, null);
        Check.That(second.Outcome == ModuleOutcome.Runs && second.Outputs[0].Value.Number == 15, "falls back to the next source");
        Check.That(second.Inputs[0].Basis == "TestSample" && second.Inputs[0].Reasons.Any(r => r.Contains("not offered", StringComparison.Ordinal)),
            "the trace says which basis answered and why the earlier source did not");

        var none = await AuthoringService.EvaluateAsync(fallback, new FakeInputs([]), "1", null, null);
        Check.That(none.Outcome == ModuleOutcome.Skipped && none.Inputs[0].Reasons.Count == 2, "no source answered: skipped, with a reason per source");

        var choose = await AuthoringService.EvaluateAsync(fallback, new FakeInputs(new() { ["surveillance:CasingPressure"] = FakeInputs.Choose("surveillance:CurrentX", "surveillance:AverageX") }), "1", null, null);
        Check.That(choose.Outcome == ModuleOutcome.NeedsSetup && choose.Inputs[0].Candidates.Count == 2, "a choice is surfaced with its candidates");

        var samples = new Dictionary<string, string> { ["Casing"] = "300" };
        var offline = await AuthoringService.EvaluateAsync(AuthoringStarters.All.Single(s => s.Id == "threshold").Draft, null, null, samples, null);
        Check.That(offline.Outcome == ModuleOutcome.Runs && offline.Outputs.Single(o => o.Name == "Status_out").Value.Text == "High", "sample values test a draft with no connection");
        var noSample = await AuthoringService.EvaluateAsync(fallback, null, null, null, null);
        Check.That(noSample.Outcome == ModuleOutcome.Unavailable, "a missing sample is unavailable, never zero");

        var plan = AuthoringStarters.All.Single(s => s.Id == "setpoint").Draft;
        var running = new FakeInputs(new() { ["surveillance:CurrentSPM"] = FakeInputs.Value("surveillance:CurrentSPM", 8) });
        var bump = await AuthoringService.EvaluateAsync(plan, running, "1", null, null);
        Check.That(running.Planned == ("IdealPF", 1m) && bump.Outputs[0].Plan is { Allowed: true }, "a set point output becomes a dry-run plan");
        Check.That(bump.Outputs[0].Note is not null && bump.Outputs[0].Note!.Contains("Nothing was sent", StringComparison.Ordinal), "the plan says nothing was sent");
        var stopped = new FakeInputs(new() { ["surveillance:CurrentSPM"] = FakeInputs.Value("surveillance:CurrentSPM", 0) });
        var idle = await AuthoringService.EvaluateAsync(plan, stopped, "1", null, null);
        Check.That(idle.Outputs[0].Value.Number == 0, "a stopped well proposes no change");
        var skippedPlan = new FakeInputs([]);
        await AuthoringService.EvaluateAsync(plan, skippedPlan, "1", null, null);
        Check.That(skippedPlan.Planned is null, "a skipped module never plans a write");
    }
}
