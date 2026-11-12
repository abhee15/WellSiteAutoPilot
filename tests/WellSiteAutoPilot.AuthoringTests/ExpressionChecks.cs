using WellSiteAutoPilot.Application.Authoring;

namespace WellSiteAutoPilot.AuthoringTests;

internal static class ExpressionChecks
{
    public static void Run()
    {
        static (string, Val) N(string name, decimal v, string? unit = null) => (name, Val.Of(v, unit));
        Check.That(Check.Eval("1 + 2 * 3").Number == 7, "operator precedence");
        Check.That(Check.Eval("(1 + 2) * 3").Number == 9, "parentheses");
        Check.That(Check.Eval("-A + 10", N("A", 4)).Number == 6, "unary minus");
        Check.That(Check.Eval("10 % 4").Number == 2, "modulo");
        Check.That(Check.Eval("A > 3 && B < 2", N("A", 4), N("B", 1)).Flag == true, "and of comparisons");
        Check.That(Check.Eval("!(A > 3)", N("A", 4)).Flag == false, "not");
        Check.That(Check.Eval("A != 4", N("A", 4)).Flag == false, "not equal");
        Check.That(Check.Eval("\"a\" + \"b\"").Text == "ab", "text concat");
        Check.That(Check.Eval("if(A > 3, \"High\", \"Normal\")", N("A", 4)).Text == "High", "if text");
        Check.That(Check.Eval("round(2.345, 2)").Number == 2.35m, "round half away from zero");
        Check.That(Check.Eval("clamp(150, 0, 100)").Number == 100, "clamp");
        Check.That(Check.Eval("avg(2, 4, 6)").Number == 4 && Check.Eval("min(3, 1, 2)").Number == 1 && Check.Eval("max(3, 1, 2)").Number == 3, "avg min max");
        Check.That(Check.Eval("0.1 + 0.2").Number == 0.3m, "decimal arithmetic has no binary rounding noise");

        // A missing value is not zero. It stays missing through arithmetic and comparison.
        var missing = ("M", Val.Missing("no sensor"));
        Check.That(!Check.Eval("M + 1", missing).Available, "missing + 1 is missing");
        Check.That(Check.Eval("M + 1", missing).Reason == "no sensor", "the reason travels with the missing value");
        Check.That(!Check.Eval("M > 0", missing).Available, "missing compared is missing, not false");
        Check.That(!Check.Eval("M * 0", missing).Available, "missing times zero is still missing");
        Check.That(Check.Eval("0 + 0").Available && Check.Eval("0 + 0").Number == 0, "a calculated zero is a valid value");
        Check.That(!Check.Eval("1 / 0").Available, "division by zero is unavailable, not infinity");
        Check.That(Check.Eval("coalesce(M, 7)", missing).Number == 7, "coalesce skips a missing value");
        Check.That(Check.Eval("has(M)", missing).Flag == false && Check.Eval("has(A)", N("A", 0)).Flag == true, "has() tells missing from zero");

        Check.That(Check.Eval("false && M", missing).Flag == false, "false && missing is false");
        Check.That(Check.Eval("true || M", missing).Flag == true, "true || missing is true");
        Check.That(!Check.Eval("true && M", missing).Available, "true && missing stays missing");
        Check.That(Check.Eval("if(true, 1, M)", missing).Number == 1, "if() does not evaluate the branch it skips");

        Check.That(!Check.Eval("\"a\" * 2").Available, "text times number is unavailable");
        Check.That(!Check.Eval("if(1, 2, 3)").Available, "if() needs a true/false condition");

        var refs = ModuleExpression.Parse("A + B * A + min(C, 1)").References;
        Check.That(refs.SequenceEqual(["A", "B", "C"]), "references are listed once in first-use order");

        foreach (var bad in new[] { "", "   ", "1 +", "(1", "1 2", "foo(1)", "sin(1)", "A $ B", "\"open", "1..2", new string('1', 501) })
            Check.That(Check.Fails(bad), $"'{(bad.Length > 20 ? bad[..20] : bad)}' must be rejected");

        Check.That(Val.Parse("12.5").Number == 12.5m && Val.Parse("true").Flag == true && Val.Parse("abc").Text == "abc" && !Val.Parse(" ").Available,
            "typed parsing of plain text");
        Check.That(Val.Parse("NaN").Kind == ValueKind.Text, "NaN is never a number");
        Check.That(Val.Parse("12.5", "psia").Unit == "psia", "typed parsing applies the declared unit");

        UnitChecks();
        ProvenanceChecks();
    }

    private static void UnitChecks()
    {
        static (string, Val) P(string name, decimal v, string unit) => (name, Val.Of(v, unit));
        var sum = Check.Eval("A + B", P("A", 100, "psia"), P("B", 1, "bara"));
        Check.That(sum is { Number: > 114.5m and < 114.6m, Unit: "psia" }, $"compatible units are converted, not added blindly (got {sum.Number} {sum.Unit})");
        Check.That(!Check.Eval("A + B", P("A", 100, "psia"), P("B", 1, "degF")).Available, "pressure plus temperature is refused");
        Check.That(!Check.Eval("A + B", P("A", 100, "psia"), P("B", 10, "psig")).Available, "absolute and gauge pressure are not silently mixed");
        Check.That(!Check.Eval("A > B", P("A", 100, "psia"), P("B", 1, "m")).Available, "comparing different dimensions is refused");
        Check.That(Check.Eval("A > B", P("A", 100, "psia"), P("B", 1, "bara")).Flag == true, "comparison converts first: 100 psia > 1 bar");
        Check.That(!Check.Eval("A * B", P("A", 2, "psia"), P("B", 3, "psia")).Available, "multiplying two quantities with units is refused");
        Check.That(Check.Eval("A * 2", P("A", 3, "psia")) is { Number: 6, Unit: "psia" }, "scaling keeps the unit");
        Check.That(Check.Eval("A / B", P("A", 6, "psia"), P("B", 3, "psia")) is { Number: 2, Unit: null }, "same unit divided is a plain ratio");
        Check.That(!Check.Eval("10 / A", P("A", 2, "psia")).Available, "a plain number divided by a quantity is refused");
        Check.That(Check.Eval("abs(A)", P("A", -5, "psia")) is { Number: 5, Unit: "psia" }, "functions keep units");
        Check.That(Units.TryConvert(32, "degF", "degC", out var c, out _) && c == 0, "temperature conversion handles the offset");
        Check.That(!Units.TryConvert(10, "psia", "psig", out _, out var why) && why!.Contains("psig", StringComparison.Ordinal), "psia to psig needs atmospheric pressure, so it is refused with a reason");
    }

    private static void ProvenanceChecks()
    {
        var old = Val.Of(1m, "psia") with { AsOf = FakeInputs.Now.AddMinutes(-30), Quality = DataQuality.Good };
        var fresh = Val.Of(2m, "psia") with { AsOf = FakeInputs.Now.AddSeconds(-5), Quality = DataQuality.Uncertain };
        var sum = Check.Eval("A + B", ("A", old), ("B", fresh));
        Check.That(sum.AsOf == old.AsOf, "a result is only as fresh as its oldest input");
        Check.That(sum.Quality == DataQuality.Uncertain, "a result is only as good as its worst input");
    }
}
