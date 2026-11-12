using WellSiteAutoPilot.Application.Authoring;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.AuthoringTests;

internal static class DraftChecks
{
    private static ModuleDraft Draft(
        IEnumerable<DraftInput>? inputs = null, IEnumerable<DraftParameter>? parameters = null,
        IEnumerable<DraftStep>? steps = null, IEnumerable<DraftOutput>? outputs = null, IEnumerable<TestCase>? tests = null) =>
        new("Test", null, (inputs ?? []).ToList(), (parameters ?? []).ToList(), (steps ?? []).ToList(), (outputs ?? []).ToList(), tests?.ToList());

    private static DraftInput In(string name, string quantity = "CasingPressure", string? unit = "psia", bool required = true, int? maxAge = null, bool uncertain = false) =>
        new(name, quantity, unit, null, maxAge, uncertain, required);

    private static bool Reports(ModuleDraft draft, string node, string text) =>
        DraftAnalyzer.Analyze(draft).Problems.Any(p => p.Severity == "Error" && p.Node == node && p.Message.Contains(text, StringComparison.OrdinalIgnoreCase));

    private static bool Warns(ModuleDraft draft, string node, string text) =>
        DraftAnalyzer.Analyze(draft).Problems.Any(p => p.Severity == "Warning" && p.Node == node && p.Message.Contains(text, StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync()
    {
        Starters();
        Recommendations();
        RuleTables();
        await IdleTimeHistoryAsync();
        Evaluation();
        await Acceptance();
        await ServiceAsync();
        await SetPointsAsync();
    }

    private static async Task IdleTimeHistoryAsync()
    {
        var draft = AuthoringStarters.All.Single(s => s.Id == "idle-time").Draft;
        Check.That(DraftAnalyzer.Analyze(draft).Valid, "the idle time starter is valid with history inputs");
        var today = new DateTimeOffset(2026, 5, 11, 7, 0, 0, TimeSpan.Zero);
        var bad = draft with { Inputs = [.. draft.Inputs.Take(1), draft.Inputs[1] with { History = new(0, "NoSuchParameter") }, .. draft.Inputs.Skip(2)] };
        Check.That(Reports(bad, "ProdBefore", "Time of day") || Reports(bad, "Prod", "Time of day") || Reports(bad, "ProdBefore", "HH:mm"), "an unreadable history time of day is reported");

        HistoricalReadResult Day(string quantity, DateTimeOffset end) => quantity switch
        {
            "InferredProductionYesterday" => FakeInputs.Samples(quantity, "bbl/d", false, (end.AddMinutes(-5), end.Day == 11 ? 80 : 100)),
            "CyclesYesterday" => FakeInputs.Samples(quantity, "", false, (end.AddMinutes(-9), 19), (end.AddMinutes(-1), 20)),
            _ => throw new InvalidOperationException(quantity)
        };
        var answers = new Dictionary<string, ResolvePreview> { ["IdleTime"] = FakeInputs.Value("IdleTime", 3600, unit: "s") };
        var fake = new FakeInputs(answers) { History = Day };
        var run = await AuthoringService.EvaluateAsync(draft, fake, "1596", null, null, null, new FixedClock(FakeInputs.Now));
        Check.That(run.Outcome == ModuleOutcome.Runs, "idle time runs from history: " + string.Join("; ", run.Inputs.Select(i => i.Name + "=" + i.Resolution)));
        Check.That(run.Outputs[0].Value.Number == 30m, "idle time falls 60 -> 30 min when production fell; the controller's seconds were converted to minutes");
        Check.That(fake.HistoryAsked.Any(a => a.End == today && a.Start == today.AddMinutes(-60)) && fake.HistoryAsked.Any(a => a.End == today.AddDays(-1)),
            "today's and the day before's gauge-off windows are read in site time");
        Check.That(run.Inputs.First(i => i.Name == "Cycles").Value.Number == 20m, "the last reading in the window is used");
        var proposal = run.Outputs.Single(o => o.Name == "IdleTime_proposal");
        Check.That(proposal.Proposed == false && proposal.Checks!.Any(c => c.Name == "Evidence quality" && c.Status == "Fail"), "a proposal resting on saved or uncertain readings is refused: writing needs a live read");

        var gap = new FakeInputs(answers) { History = (q, end) => q == "InferredProductionYesterday" && end.Day == 10 ? FakeInputs.Samples(q, "bbl/d", false) : Day(q, end) };
        var missing = await AuthoringService.EvaluateAsync(draft, gap, "1596", null, null, null, new FixedClock(FakeInputs.Now));
        Check.That(missing.Outcome != ModuleOutcome.Runs && !missing.Outputs[0].Value.Available, "no history for the day before stops the run instead of counting as zero");
        var cut = new FakeInputs(answers) { History = (q, end) => q == "CyclesYesterday" ? FakeInputs.Samples(q, "", true, (end.AddMinutes(-1), 20)) : Day(q, end) };
        var truncated = await AuthoringService.EvaluateAsync(draft, cut, "1596", null, null, null, new FixedClock(FakeInputs.Now));
        Check.That(truncated.Outcome != ModuleOutcome.Runs, "a truncated window is never treated as complete");

        var zone = TimeZoneInfo.CreateCustomTimeZone("site", TimeSpan.FromHours(-5), "site", "site");
        var shifted = new FakeInputs(answers) { History = Day };
        await AuthoringService.EvaluateAsync(draft, shifted, "1596", null, null, null, new FixedClock(FakeInputs.Now), zone);
        Check.That(shifted.HistoryAsked.Any(a => a.End == new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero)), "07:00 site time at UTC-5 is 12:00 UTC");
    }

    private static void RuleTables()
    {
        var trailing = InputHistoryWindow.Resolve(new(0, "now", 240, "avg"), new Dictionary<string, string>(), new DateTimeOffset(2026, 5, 11, 15, 30, 12, 345, TimeSpan.Zero), TimeZoneInfo.Utc, out _);
        Check.That(trailing is { } t && t.End == new DateTimeOffset(2026, 5, 11, 15, 30, 12, TimeSpan.Zero) && t.Start == t.End.AddHours(-4), "a 'now' window trails the run by whole seconds");
        Check.That(InputHistoryWindow.Validate(new(1, "now"), []) is not null, "a window that ends now cannot go days back");
        var draft = new ModuleDraft("Gates", null,
            [In("Fillage", "PumpFillage", "%", maxAge: 600), In("Cycles", "CyclesYesterday", null)],
            [new("FillageTarget", "70", "%", Min: 0, Max: 100), new("CyclesAllowed", "10")],
            [new("Decision", "\"hold\"", Rules:
            [
                new("Cycles > CyclesAllowed", "\"reduce\"", "Too many cycles yesterday"),
                new("Fillage < FillageTarget", "\"lower\"", "Fillage is below target")
            ])],
            [new("Result", "Decision")]);
        Check.That(DraftAnalyzer.Analyze(draft).Valid, "a rule table is a valid step");
        string Run(string fillage, string cycles, out string? why)
        {
            var run = AuthoringService.EvaluateAsync(draft, null, null, new Dictionary<string, string> { ["Fillage"] = fillage, ["Cycles"] = cycles }, null).GetAwaiter().GetResult();
            why = run.Steps.Single().Matched;
            return run.Steps.Single().Value.Text ?? run.Steps.Single().Value.Reason ?? "";
        }
        Check.That(Run("60", "12", out var first) == "reduce" && first == "Rule 1: Too many cycles yesterday", "the first matching row decides, even when a later row also matches");
        Check.That(Run("60", "3", out var second) == "lower" && second == "Rule 2: Fillage is below target", "a later row decides when earlier rows do not match");
        Check.That(Run("85", "3", out var none) == "hold" && none!.StartsWith("No rule matched", StringComparison.Ordinal), "no match gives the otherwise value, and says so");

        var unknown = draft with { Inputs = [In("Fillage", "PumpFillage", "%", maxAge: 600), In("Cycles", "CyclesYesterday", null, required: false)] };
        var undecided = AuthoringService.EvaluateAsync(unknown, null, null, new Dictionary<string, string> { ["Fillage"] = "60" }, null).GetAwaiter().GetResult().Steps.Single();
        Check.That(!undecided.Value.Available && undecided.Matched!.Contains("could not be decided", StringComparison.Ordinal),
            "a gate that cannot be decided stops the table instead of being skipped");

        var typo = draft with { Steps = [new("Decision", "\"hold\"", Rules: [new("Cycle > CyclesAllowed", "\"x\"", "r")])] };
        Check.That(Reports(typo, "Decision", "Rule 1 condition"), "a rule that names nothing is reported against its row");
        var unexplained = draft with { Steps = [new("Decision", "\"hold\"", Rules: [new("Cycles > CyclesAllowed", "\"x\"")])] };
        Check.That(Warns(unexplained, "Decision", "no reason"), "a row without a reason is flagged");
    }

    private static void Starters()
    {
        foreach (var starter in AuthoringStarters.All)
        {
            var analysis = DraftAnalyzer.Analyze(starter.Draft);
            Check.That(analysis.Valid, $"starter '{starter.Id}' must be valid: " + string.Join("; ", analysis.Problems.Select(p => p.Node + ": " + p.Message)));
            Check.That(!analysis.Problems.Any(p => p.Severity == "Warning"), $"starter '{starter.Id}' must be warning-free: " + string.Join("; ", analysis.Problems.Select(p => p.Node + ": " + p.Message)));
            Check.That(starter.Draft.Tests is { Count: > 0 }, $"starter '{starter.Id}' ships worked examples");
            var tests = DraftTests.Run(starter.Draft);
            Check.That(tests.AllPassed, $"starter '{starter.Id}' passes its own examples: " +
                string.Join("; ", tests.Cases.Where(c => !c.Passed).SelectMany(c => c.Checks.Where(k => !k.Passed).Select(k => $"{c.Name}/{k.Output} expected {k.Expected} got {k.Actual}"))));
            ManifestMapper.Validate(starter.Draft);
            var manifest = ManifestMapper.ToManifest(starter.Draft);
            Check.That(manifest.DataRequirements.Count == starter.Draft.Inputs.Count, $"starter '{starter.Id}' maps every input to a data requirement");
            Check.That(manifest.CommandRequirements.Count == starter.Draft.Outputs.Count(o => o.Kind == "setpoint"), $"starter '{starter.Id}' maps set points to command requirements");
        }
        Check.That(AuthoringStarters.All.Select(s => s.Id).Distinct().Count() == AuthoringStarters.All.Count, "starter ids are unique");
        Check.That(AuthoringStarters.All.SelectMany(s => s.Draft.Inputs).All(i => !i.Quantity.Contains(':', StringComparison.Ordinal)),
            "starters ask for quantities, never provider points, so they travel between systems");
    }

    private static ModuleRun RunSample(string starter, Dictionary<string, string> inputs, Dictionary<string, string>? parameters = null)
    {
        var draft = AuthoringStarters.All.Single(s => s.Id == starter).Draft;
        return AuthoringService.EvaluateAsync(draft, null, null, inputs, parameters).GetAwaiter().GetResult();
    }

    // The wording and outcome codes of the shipped CygNet sample modules (modules\samples\cygnet), so an authored module can replace them.
    private static void Recommendations()
    {
        var low = RunSample("fillage-alert", new() { ["Fillage"] = "25" }).Outputs.Single(o => o.Name == "Low_fillage");
        Check.That(low.Advice is { Code: "PUMP_FILLAGE_LOW", Title: "Pump fillage is low" }, "a low fillage recommends a review with the sample's outcome code");
        Check.That(low.Advice!.Detail == "Pump fillage is 25 %, below the 40 % threshold. Review the well for pump-off or a gas-locked pump.",
            "the recommendation text is filled from the calculation: " + low.Advice.Detail);
        Check.That(RunSample("fillage-alert", new() { ["Fillage"] = "72" }).Outputs.Single(o => o.Name == "Low_fillage").Advice is null, "a healthy fillage recommends nothing");

        var loaded = RunSample("load-alert", new() { ["Amps"] = "75", ["Rod"] = "18000" }).Outputs.Single();
        Check.That(loaded.Advice!.Detail.Contains("motor current 75 A (limit 60 A)", StringComparison.Ordinal), "a load recommendation names the limit that was exceeded");

        var refused = RunSample("fillage-alert", new() { ["Fillage"] = "140" });
        Check.That(refused.Outcome == ModuleOutcome.Unavailable && refused.Outputs.Single(o => o.Name == "Low_fillage").Advice is null,
            "a reading outside the input's valid range is distrusted and never judged");

        var tooStrict = RunSample("fillage-alert", new() { ["Fillage"] = "50" }, new() { ["LowFillage"] = "250" });
        Check.That(tooStrict.Outcome == ModuleOutcome.Invalid, "an override outside the parameter's limits is refused");
        Check.That(RunSample("fillage-alert", new() { ["Fillage"] = "50" }, new() { ["LowFillage"] = "60" }).Outputs.Single(o => o.Name == "Low_fillage").Advice is not null,
            "an override inside the limits applies");

        var draft = AuthoringStarters.All.Single(s => s.Id == "fillage-alert").Draft;
        var typo = draft with { Outputs = [.. draft.Outputs.Where(o => o.Kind != "recommendation"),
            new DraftOutput("R", "IsLow", "recommendation", Title: "Low", Detail: "Fillage is {Fillge}.")] };
        Check.That(Reports(typo, "R", "{Fillge}"), "a placeholder that names nothing is reported");
        var badDefault = draft with { Parameters = [new("LowFillage", "150", "%", Min: 0, Max: 100)] };
        Check.That(Reports(badDefault, "LowFillage", "above the maximum"), "a default outside its own limits is reported");
        Check.That(Reports(draft with { Outputs = [new DraftOutput("R", "IsLow", "recommendation", Code: "low-fillage", Title: "Low")] }, "R", "capital letters"),
            "an outcome code must be capitals, digits and underscores");
        Check.That(Units.TryConvert(1m, "kN", "lbf", out var pounds, out _) && Math.Abs(pounds - 224.808943m) < 0.001m && Units.Normalize("lb") == "lbf", "force units convert and lb means lbf");
    }

    private static void Analyzer()
    {
        var ok = Draft([In("A")], [], [new DraftStep("S", "A + 1")], [new DraftOutput("O", "S")]);
        Check.That(DraftAnalyzer.Analyze(ok).Valid, "a simple chain is valid");
        Check.That(DraftAnalyzer.Analyze(ok).Order.SequenceEqual(["S"]), "steps are ordered");

        Check.That(Reports(Draft([In("A")], [], [], [new DraftOutput("O", "B")]), "O", "'B' is not"), "unknown reference is reported at the output");
        Check.That(Reports(Draft([In("A")], [], [new DraftStep("S", "A +")], [new DraftOutput("O", "S")]), "S", "ends too soon"), "parse error is reported at the step");
        Check.That(Reports(Draft([In("A"), In("A", "TubingPressure")], [], [], [new DraftOutput("O", "A")]), "A", "already used"), "duplicate names");
        Check.That(Reports(Draft([In("1bad")], [], [], [new DraftOutput("O", "1")]), "1bad", "start with a letter"), "bad identifier");
        Check.That(Reports(Draft([In("if")], [], [], [new DraftOutput("O", "1")]), "if", "reserved"), "function names are reserved");
        Check.That(Reports(Draft([], [], [], []), "module", "at least one output"), "an output is required");
        Check.That(Reports(Draft([In("A")], [], [], [new DraftOutput("O1", "A"), new DraftOutput("O2", "O1")]), "O2", "outputs cannot feed"), "outputs cannot be used as inputs");
        Check.That(Reports(Draft([In("A", "")], [], [], [new DraftOutput("O", "A")]), "A", "what this input measures"), "an input needs a quantity");
        Check.That(Reports(Draft([new DraftInput("A", "CasingPressure", "psia", "Bogus")], [], [], [new DraftOutput("O", "A")]), "A", "unknown basis"), "basis is validated");
        Check.That(Reports(Draft([In("A", maxAge: 0)], [], [], [new DraftOutput("O", "A")]), "A", "greater than zero"), "a maximum age must be positive");
        Check.That(Warns(Draft([In("A", "surveillance:CasingPressure")], [], [], [new DraftOutput("O", "A")]), "A", "provider point"), "a provider point in the definition is a portability warning");
        Check.That(Warns(Draft([In("A", unit: "furlongs")], [], [], [new DraftOutput("O", "A")]), "A", "not a unit"), "an unknown unit is a warning");

        Check.That(Reports(Draft([In("A")], [], [], [new DraftOutput("O", "A", "setpoint")]), "O", "Name the set point"), "a set point output needs a target");
        Check.That(Warns(Draft([In("A")], [], [], [new DraftOutput("O", "A", "setpoint", Target: "X")]), "O", "No minimum or maximum"), "an unbounded set point output is warned about");
        Check.That(Reports(Draft([In("A")], [], [], [new DraftOutput("O", "A", "setpoint", Target: "X", Limits: new(10, 5))]), "O", "minimum is above"), "inverted limits");
        Check.That(Reports(Draft([In("A")], [], [], [new DraftOutput("O", "A", "setpoint", Target: "X", Limits: new(0, 5, 0))]), "O", "maximum step"), "a zero step limit");
        Check.That(Reports(Draft([In("A")], [], [], [new DraftOutput("O", "A", "setpoint", Target: "X", Limits: new(0, 5), When: "Nope")]), "O", "'Nope' is not"), "a condition may only use known names");

        var cycle = Draft([In("A")], [], [new DraftStep("P", "Q + A"), new DraftStep("Q", "P")], [new DraftOutput("O", "Q")]);
        Check.That(Reports(cycle, "P", "depends on itself") && Reports(cycle, "Q", "depends on itself"), "a cycle is reported on every step in it");
        Check.That(Reports(Draft([], [], [new DraftStep("S", "S + 1")], [new DraftOutput("O", "S")]), "S", "cannot use itself"), "self reference");

        var unused = DraftAnalyzer.Analyze(Draft([In("A"), In("B", "TubingPressure")], [], [], [new DraftOutput("O", "A")]));
        Check.That(unused.Valid && unused.Problems.Any(p => p.Severity == "Warning" && p.Node == "B"), "an unused input is a warning, not an error");

        var graph = DraftAnalyzer.Analyze(Draft([In("A")], [], [new DraftStep("S", "A * 2")], [new DraftOutput("O", "S + A")])).Graph.ToDictionary(g => g.Name);
        Check.That(graph["A"].UsedBy.SequenceEqual(["O", "S"]), "tracing downstream from an input finds its consumers");
        Check.That(graph["O"].DependsOn.OrderBy(x => x).SequenceEqual(["A", "S"]), "tracing upstream from an output finds its sources");

        Check.That(Reports(Draft([In("A")], [], [], [new DraftOutput("O", "A")], [new TestCase("t", new() { ["Nope"] = "1" }, new() { ["O"] = "1" })]), "module", "not an input"), "an example may only set inputs");
        Check.That(Reports(Draft([In("A")], [], [], [new DraftOutput("O", "A")], [new TestCase("t", new() { ["A"] = "1" }, new() { ["Nope"] = "1" })]), "module", "not an output"), "an example may only expect outputs");
        Check.That(Reports(Draft([In("A")], [], [], [new DraftOutput("O", "A")], [new TestCase("t", new() { ["A"] = "1" }, [])]), "module", "does not say what to expect"), "an example must expect something");
        Check.That(Reports(new ModuleDraft("T", null, [In("A")], [], [], [new DraftOutput("O", "A")], SchemaVersion: 99), "module", "schema version"), "a newer schema is refused rather than half understood");
        Check.That(Reports(new ModuleDraft("T", null, [In("A")], [], [], [new DraftOutput("O", "A")], LanguageVersion: 99), "module", "language version"), "a newer language is refused");

        var one = Draft([In("A")], [], [], [new DraftOutput("O", "A")]);
        var same = Draft([In("A")], [], [], [new DraftOutput("O", "A")]);
        var changed = Draft([In("A")], [new DraftParameter("P", "1")], [], [new DraftOutput("O", "A")]);
        var relabelled = new ModuleDraft("Another name", "A description", [In("A") with { Label = "Casing" }], [], [], [new DraftOutput("O", "A", Label: "Out")],
            [new TestCase("t", new() { ["A"] = "1" }, new() { ["O"] = "1" })], Version: "9.9.9");
        Check.That(DraftAnalyzer.Fingerprint(one) == DraftAnalyzer.Fingerprint(same) && DraftAnalyzer.Fingerprint(one).Length == 64, "the fingerprint is stable and is a full SHA-256");
        Check.That(DraftAnalyzer.Fingerprint(one) != DraftAnalyzer.Fingerprint(changed), "any behavioural edit changes the fingerprint");
        Check.That(DraftAnalyzer.Fingerprint(one) == DraftAnalyzer.Fingerprint(relabelled), "names, labels, descriptions, versions and examples do not change what the module does");
        Check.That(DraftAnalyzer.Fingerprint(one) != DraftAnalyzer.Fingerprint(Draft([In("A", unit: "psig")], [], [], [new DraftOutput("O", "A")])), "a unit change is a behavioural change");
    }

    private static InputTrace Trace(string name, string resolution, Val value, bool required = true, params string[] reasons) =>
        new(name, resolution, value, null, null, null, null, reasons, [], required);

    private static void Evaluation()
    {
        var draft = AuthoringStarters.All.Single(s => s.Id == "threshold").Draft;
        var run = DraftEvaluator.Evaluate(draft, [Trace("Casing", "Resolved", Val.Of(300, "psia"))]);
        Check.That(run.Outcome == ModuleOutcome.Runs, "resolved inputs run");
        Check.That(run.Steps.Single(s => s.Name == "IsHigh").Value.Flag == true, "step value");
        Check.That(run.Outputs.Single(o => o.Name == "Status_out").Value.Text == "High", "output follows the steps");
        Check.That(run.Steps.Single(s => s.Name == "Margin").DependsOn.OrderBy(x => x).SequenceEqual(["Casing", "HighLimit"]), "each step records what it read");
        Check.That(run.Outputs.Single(o => o.Name == "Margin_out").Value is { Number: -50, Unit: "psia" }, "the margin carries its unit");

        var lowered = DraftEvaluator.Evaluate(draft, [Trace("Casing", "Resolved", Val.Of(300, "psia"))], new Dictionary<string, string> { ["HighLimit"] = "400" });
        Check.That(lowered.Outputs.Single(o => o.Name == "Status_out").Value.Text == "Normal", "a parameter override changes the result");
        Check.That(lowered.Fingerprint == run.Fingerprint, "overriding a parameter for a test does not change the draft");

        var zero = DraftEvaluator.Evaluate(draft, [Trace("Casing", "Resolved", Val.Of(0, "psia"))]);
        Check.That(zero.Outputs.Single(o => o.Name == "Margin_out").Value.Number == 250, "a real zero input is a valid value");

        var skipped = DraftEvaluator.Evaluate(draft, [Trace("Casing", "NotApplicable", Val.Missing("no casing"), true, "no casing")]);
        Check.That(skipped.Outcome == ModuleOutcome.Skipped && skipped.Outputs.All(o => !o.Value.Available), "a missing required input skips; outputs stay unavailable, not zero");
        Check.That(DraftEvaluator.Evaluate(draft, [Trace("Casing", "NeedsSelection", Val.Missing("pick"))]).Outcome == ModuleOutcome.NeedsSetup, "needs selection asks the author to choose");
        Check.That(DraftEvaluator.Evaluate(draft, [Trace("Casing", "UnitMismatch", Val.Missing("units"))]).Outcome == ModuleOutcome.NeedsSetup, "a unit mismatch asks the author to fix the unit");
        Check.That(DraftEvaluator.Evaluate(draft, [Trace("Casing", "CannotDetermine", Val.Missing("offline"))]).Outcome == ModuleOutcome.Unavailable, "an unreachable provider is not the same as not applicable");
        Check.That(DraftEvaluator.Evaluate(draft, [Trace("Casing", "Stale", Val.Missing("old"))]).Outcome == ModuleOutcome.Unavailable, "a stale reading makes the module unavailable");
        Check.That(DraftEvaluator.Evaluate(draft, [Trace("Casing", "BadQuality", Val.Missing("bad"))]).Outcome == ModuleOutcome.Unavailable, "a bad reading makes the module unavailable");

        var optional = AuthoringStarters.All.Single(s => s.Id == "snapshot").Draft;
        var withoutFillage = DraftEvaluator.Evaluate(optional,
            [Trace("Casing", "Resolved", Val.Of(1, "psia")), Trace("Tubing", "Resolved", Val.Of(2, "psia")), Trace("Fillage", "NotApplicable", Val.Missing("none"), false)]);
        Check.That(withoutFillage.Outcome == ModuleOutcome.Runs && withoutFillage.Outputs.Single(o => o.Name == "Fillage_out").Value.Number == -1,
            "an optional input that is missing does not stop the module, and coalesce supplies the default");

        var broken = Draft([In("A")], [], [], [new DraftOutput("O", "Nope")]);
        Check.That(DraftEvaluator.Evaluate(broken, []).Outcome == ModuleOutcome.Invalid, "an invalid draft is never evaluated");

        var wrongUnit = Draft([In("A")], [], [], [new DraftOutput("O", "A", Unit: "degF")]);
        Check.That(!DraftEvaluator.Evaluate(wrongUnit, [Trace("A", "Resolved", Val.Of(5, "psia"))]).Outputs[0].Value.Available, "an output in the wrong dimension is unavailable, not relabelled");
        var converted = Draft([In("A")], [], [], [new DraftOutput("O", "A", Unit: "kPaa")]);
        Check.That(DraftEvaluator.Evaluate(converted, [Trace("A", "Resolved", Val.Of(1, "psia"))]).Outputs[0].Value is { Unit: "kPaa", Number: > 6.8m and < 6.9m }, "an output in a compatible unit is converted");
    }

    private static async Task Acceptance()
    {
        var now = FakeInputs.Now;
        var input = In("A", maxAge: 600);

        var good = InputAcceptance.Accept(input, 100, "psia", now.AddSeconds(-60), "Good", now);
        Check.That(good.Resolution == "Resolved" && good.Value is { Number: 100, Unit: "psia", Quality: DataQuality.Good } && good.AgeSeconds == 60, "a fresh good reading is accepted with its age");
        Check.That(InputAcceptance.Accept(input, 100, "psia", now.AddSeconds(-601), "Good", now).Resolution == "Stale", "a reading older than the maximum age is refused");
        Check.That(InputAcceptance.Accept(input, 100, "psia", now.AddSeconds(-600), "Good", now).Resolution == "Resolved", "a reading exactly at the limit is accepted");
        Check.That(InputAcceptance.Accept(In("A"), 100, "psia", now.AddDays(-30), "Good", now).Resolution == "Resolved", "with no maximum age, any age is accepted");
        Check.That(InputAcceptance.Accept(input, 100, "psia", now.AddHours(1), "Good", now).Resolution == "Stale", "a reading time-stamped in the future is not trusted");
        Check.That(InputAcceptance.Accept(input, 100, "psia", now, "Bad", now).Resolution == "BadQuality", "a bad reading is refused");
        Check.That(InputAcceptance.Accept(input, 100, "psia", now, "Uncertain", now).Resolution == "BadQuality", "an uncertain reading is refused unless the input accepts it");
        var tolerant = InputAcceptance.Accept(In("A", uncertain: true), 100, "psia", now, "Uncertain", now);
        Check.That(tolerant.Resolution == "Resolved" && tolerant.Value.Quality == DataQuality.Uncertain, "an input that accepts uncertain readings keeps the doubt on the value");
        Check.That(InputAcceptance.Accept(input, 100, "psia", now, null, now).Resolution == "BadQuality", "an unreported quality is not assumed good");

        var converted = InputAcceptance.Accept(In("A", unit: "kPaa"), 1, "bara", now, "Good", now);
        Check.That(converted.Value is { Unit: "kPaa", Number: 100 }, "a compatible unit is converted to the unit the module declared");
        Check.That(InputAcceptance.Accept(In("A", unit: "psia"), 5, "psig", now, "Good", now).Resolution == "UnitMismatch", "gauge pressure is never silently taken as absolute");
        Check.That(InputAcceptance.Accept(In("A", unit: "psia"), 5, "psi", now, "Good", now).Resolution == "UnitMismatch", "a reading with no reference is never taken as absolute");
        Check.That(InputAcceptance.Accept(In("A", unit: "psi"), 5, "psia", now, "Good", now) is { Resolution: "Resolved", Value.Number: 5 }, "an input that does not care about the reference accepts an absolute reading");
        Check.That(InputAcceptance.Accept(In("A", unit: "psi"), 5, "kPag", now, "Good", now).Value.Number is > 0.72m and < 0.73m, "and converts it to the scale it expects");
        Check.That(InputAcceptance.Accept(In("A", unit: "psia"), 5, "degF", now, "Good", now).Resolution == "UnitMismatch", "a different dimension is refused");
        Check.That(InputAcceptance.Accept(In("A", unit: null), 5, "psia", now, "Good", now).Value.Unit == "psia", "an input with no declared unit adopts the provider's");
        await Task.CompletedTask;
    }

    private static async Task ServiceAsync()
    {
        var clock = new FixedClock(FakeInputs.Now);
        var fallback = AuthoringStarters.All.Single(s => s.Id == "fallback");
        var bindings = fallback.Bindings;
        Task<ModuleRun> Run(FakeInputs inputs, ModuleBindings? b = null, ModuleDraft? d = null) =>
            AuthoringService.EvaluateAsync(d ?? fallback.Draft, inputs, "1", null, null, b ?? bindings, clock);

        var live = new FakeInputs(new() { ["CasingPressure"] = FakeInputs.Value("surveillance.CasingPressure", 120) });
        var first = await Run(live);
        Check.That(first.Outcome == ModuleOutcome.Runs && first.Outputs[0].Value.Number == 120, "the first source that answers wins");
        Check.That(live.Asked.Count == 1, "later sources are not asked once one answered");
        Check.That(first.Inputs[0].Unit == "psi" && first.Inputs[0].AgeSeconds == 30 && first.Inputs[0].Quantity == "CasingPressure", "the trace keeps the unit, the age and the quantity");

        var wellTestOnly = new FakeInputs(new() { ["CasingPressure|TestSample"] = FakeInputs.Value("welltest.AverageCasingPressure", 15, ResolveBasis.TestSample) });
        var second = await Run(wellTestOnly);
        Check.That(second.Outcome == ModuleOutcome.Runs && second.Outputs[0].Value.Number == 15 && wellTestOnly.Asked.Count == 2, "a source that is not applicable passes to the next binding");
        Check.That(second.Inputs[0].Reasons.Any(r => r.Contains("not offered", StringComparison.Ordinal)), "the trace says why the earlier source did not answer");

        var none = await Run(new FakeInputs([]));
        Check.That(none.Outcome == ModuleOutcome.Skipped && none.Inputs[0].Reasons.Count == 2, "no source answered: skipped, with a reason per source");

        var choose = await Run(new FakeInputs(new() { ["CasingPressure"] = FakeInputs.Choose("a", "b") }));
        Check.That(choose.Outcome == ModuleOutcome.NeedsSetup && choose.Inputs[0].Candidates.Count == 2, "a choice is surfaced with its candidates");

        // Only "not applicable" falls through. A broken primary source must not be hidden by a healthy fallback.
        var broken = new FakeInputs(new() { ["CasingPressure"] = FakeInputs.Fail(ResolveOutcome.CannotDetermine, "provider offline") });
        var stopped = await Run(broken);
        Check.That(stopped.Outcome == ModuleOutcome.Unavailable && broken.Asked.Count == 1, "an unreachable first source stops the input instead of falling through");
        var misconfigured = await Run(new FakeInputs(new() { ["CasingPressure"] = FakeInputs.Fail(ResolveOutcome.Misconfigured, "bad mapping") }));
        Check.That(misconfigured.Outcome == ModuleOutcome.NeedsSetup, "a misconfigured source asks for setup");

        var stale = await Run(new FakeInputs(new() { ["CasingPressure"] = FakeInputs.Value("s", 120, ageSeconds: 7200) }), d: fallback.Draft with { Inputs = [fallback.Draft.Inputs[0] with { MaxAgeSeconds = 900 }] });
        Check.That(stale.Outcome == ModuleOutcome.Unavailable && stale.Inputs[0].Resolution == "Stale" && stale.Outputs[0].Value.Available == false, "a reading that is too old stops the module");
        var gauge = await Run(new FakeInputs(new() { ["CasingPressure"] = FakeInputs.Value("s", 20, unit: "psig") }), d: fallback.Draft with { Inputs = [fallback.Draft.Inputs[0] with { Unit = "psia" }] });
        Check.That(gauge.Outcome == ModuleOutcome.NeedsSetup && gauge.Inputs[0].Resolution == "UnitMismatch", "a provider reporting gauge pressure is a unit mismatch, not a number");
        var metric = await Run(new FakeInputs(new() { ["CasingPressure"] = FakeInputs.Value("s", 1, unit: "bara") }), d: fallback.Draft with { Inputs = [fallback.Draft.Inputs[0] with { Unit = "psia" }] });
        Check.That(metric.Outcome == ModuleOutcome.Runs && metric.Outputs[0].Value.Number is > 14.5m and < 14.6m, "bar is converted to the module's psia");

        var byQuantity = new FakeInputs(new() { ["CasingPressure"] = FakeInputs.Value("s", 55) });
        var unbound = await AuthoringService.EvaluateAsync(fallback.Draft, byQuantity, "1", null, null, null, clock);
        Check.That(unbound.Outcome == ModuleOutcome.Runs && byQuantity.Asked.SequenceEqual(["CasingPressure"]), "with no bindings an input is read by its quantity");

        var samples = new Dictionary<string, string> { ["Casing"] = "300" };
        var threshold = AuthoringStarters.All.Single(s => s.Id == "threshold").Draft;
        var offline = await AuthoringService.EvaluateAsync(threshold, null, null, samples, null, null, clock);
        Check.That(offline.Mode == "sample" && offline.Outcome == ModuleOutcome.Runs && offline.Outputs.Single(o => o.Name == "Status_out").Value.Text == "High", "sample values test a draft with no connection");
        Check.That(offline.Inputs[0].Value.Unit == "psia", "a sample takes the declared unit");
        var noSample = await AuthoringService.EvaluateAsync(fallback.Draft, null, null, null, null, null, clock);
        Check.That(noSample.Outcome == ModuleOutcome.Unavailable, "a missing sample is unavailable, never zero");
    }

    private static async Task SetPointsAsync()
    {
        var clock = new FixedClock(FakeInputs.Now);
        var starter = AuthoringStarters.All.Single(s => s.Id == "setpoint");
        Task<ModuleRun> Run(FakeInputs inputs, ModuleDraft? draft = null, Dictionary<string, string>? parameters = null) =>
            AuthoringService.EvaluateAsync(draft ?? starter.Draft, inputs, "1", null, parameters, starter.Bindings, clock);
        FakeInputs Running(decimal spm = 8, decimal current = 30) =>
            new(new() { ["PumpSpm"] = FakeInputs.Value("s", spm, unit: "spm") }) { Current = current };

        var running = Running();
        var proposal = await Run(running);
        var output = proposal.Outputs[0];
        Check.That(running.Planned == ("IdealPF", 40m) && output.Plan is { Allowed: true }, "a set point output becomes a dry-run plan of the absolute value");
        Check.That(output.Proposed == true && output.Note!.Contains("Nothing was sent", StringComparison.Ordinal), "an allowed proposal says nothing was sent");
        Check.That(output.Checks!.All(c => c.Status != "Fail") && output.Checks!.Any(c => c.Name == "Condition" && c.Status == "Pass"), "every check passed and the condition is shown");
        Check.That(output.Checks!.Any(c => c.Name == "Controller range" && c.Status == "Pass"), "the controller's range is checked");

        var stopped = Running(spm: 0);
        var idle = await Run(stopped);
        Check.That(idle.Outputs[0].Proposed == false && idle.Outputs[0].Checks!.Any(c => c.Name == "Condition" && c.Status == "Hold"), "a stopped well holds the proposal");
        Check.That(stopped.Planned is null, "a held proposal never asks the controller to plan");

        var skipped = new FakeInputs([]);
        await Run(skipped);
        Check.That(skipped.Planned is null, "a skipped module never plans a write");

        var tooHigh = await Run(Running(), parameters: new() { ["Target"] = "120" });
        Check.That(tooHigh.Outputs[0].Proposed == false && tooHigh.Outputs[0].Checks!.Any(c => c.Name == "Module limits" && c.Status == "Fail"), "a value outside the module's limits is not proposed");

        var narrow = new FakeInputs(new() { ["PumpSpm"] = FakeInputs.Value("s", 8, unit: "spm") }) { RangeHigh = 35 };
        var refused = await Run(narrow);
        Check.That(refused.Outputs[0].Proposed == false && refused.Outputs[0].Checks!.Any(c => c.Name == "Controller range" && c.Status == "Fail"), "a value outside the controller's range is not proposed");

        var stepped = starter.Draft with { Outputs = [starter.Draft.Outputs[0] with { Limits = new(0, 100, 5) }] };
        var farJump = await Run(Running(current: 30), stepped);
        Check.That(farJump.Outputs[0].Proposed == false && farJump.Outputs[0].Checks!.Any(c => c.Name == "Maximum step" && c.Status == "Fail"), "a jump bigger than the step limit is not proposed");
        var smallJump = await Run(Running(current: 38), stepped);
        Check.That(smallJump.Outputs[0].Proposed == true, "a jump inside the step limit is proposed");

        var uncertain = new FakeInputs(new() { ["PumpSpm"] = FakeInputs.Value("s", 8, unit: "spm", quality: "Uncertain") });
        var doubtful = await Run(uncertain, starter.Draft with { Inputs = [starter.Draft.Inputs[0] with { AllowUncertain = true }] });
        Check.That(doubtful.Outputs[0].Proposed == false && doubtful.Outputs[0].Checks!.Any(c => c.Name == "Evidence quality" && c.Status == "Fail"),
            "even when an input tolerates uncertain readings, a set point is not proposed on doubtful evidence");

        var sample = await AuthoringService.EvaluateAsync(starter.Draft, null, null, new Dictionary<string, string> { ["Spm"] = "6" }, null, null, clock);
        Check.That(sample.Outputs[0].Proposed == true && sample.Outputs[0].Checks!.Any(c => c.Name == "Controller" && c.Status == "Info"), "a sample run proposes but says the controller was not consulted");

        var tests = DraftTests.Run(starter.Draft, [new TestCase("wrong", new() { ["Spm"] = "6" }, new() { ["IdealPF_proposed"] = "41" })]);
        Check.That(!tests.AllPassed && tests.Cases[0].Checks[0].Actual.StartsWith("40", StringComparison.Ordinal), "a failing example reports what was expected and what the module produced");
        var tolerant = DraftTests.Run(starter.Draft, [new TestCase("close", new() { ["Spm"] = "6" }, new() { ["IdealPF_proposed"] = "40.4" }, Tolerance: 0.5m)]);
        Check.That(tolerant.AllPassed, "an example can state a tolerance");
    }
}
