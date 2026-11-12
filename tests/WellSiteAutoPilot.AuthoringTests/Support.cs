using WellSiteAutoPilot.Application.Authoring;
using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.AuthoringTests;

internal static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Authoring check failed: " + message);
    }

    public static Val Eval(string expression, params (string Name, Val Value)[] values)
    {
        var map = values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return ModuleExpression.Parse(expression).Evaluate(n => map.TryGetValue(n, out var v) ? v : Val.Missing($"'{n}' has no value"));
    }

    public static bool Fails(string expression)
    {
        try { ModuleExpression.Parse(expression); return false; }
        catch (ExpressionException) { return true; }
    }
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>A provider double that answers per input name, so draft behaviour is tested without a live system.</summary>
internal sealed class FakeInputs(Dictionary<string, ResolvePreview> answers) : IAuthoringInputs
{
    public static readonly DateTimeOffset Now = new(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);

    public List<string> Asked { get; } = [];
    public (string SetPoint, decimal Proposed)? Planned { get; private set; }
    public decimal Current { get; init; } = 30;
    public decimal RangeLow { get; init; }
    public decimal RangeHigh { get; init; } = 100;

    public Func<string, DateTimeOffset, HistoricalReadResult>? History { get; init; }
    public List<(string Quantity, DateTimeOffset Start, DateTimeOffset End)> HistoryAsked { get; } = [];

    public Task<HistoricalReadResult> ReadHistoryAsync(string externalId, string quantity, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken)
    {
        HistoryAsked.Add((quantity, startUtc, endUtc));
        return Task.FromResult(History?.Invoke(quantity, endUtc) ?? throw new InvalidOperationException("No history was set up."));
    }

    public static HistoricalReadResult Samples(string quantity, string unit, bool truncated, params (DateTimeOffset At, decimal Value)[] samples) =>
        new(quantity, "fake", null, "Raw", default, default,
            samples.Select(s => new EngineeringValue(quantity, s.Value, unit, s.At, "Unknown", "fake")).ToList(), truncated, []);

    public Task<ResolvePreview> ResolveAsync(string externalId, string input, ResolveBasis? basis, CancellationToken cancellationToken)
    {
        Asked.Add(input);
        if (answers.TryGetValue($"{input}|{basis}", out var answer) || answers.TryGetValue(input, out answer)) return Task.FromResult(answer);
        return Task.FromResult(new ResolvePreview(ResolveResult.Failed(ResolveOutcome.NotApplicable, $"{input} is not offered on this asset."), null, null));
    }

    public Task<SetPointWritePlan> PlanSetPointAsync(string externalId, string setPoint, decimal proposed, CancellationToken cancellationToken)
    {
        Planned = (setPoint, proposed);
        var allowed = proposed >= RangeLow && proposed <= RangeHigh;
        return Task.FromResult(new SetPointWritePlan(externalId, setPoint, "Ideal PF", "%", Current, proposed, RangeLow, RangeHigh, allowed,
            allowed ? null : "Outside the controller's range."));
    }

    public static ResolvePreview Value(
        string source, decimal value, ResolveBasis basis = ResolveBasis.Current, string unit = "psia", int ageSeconds = 30, string quality = "Good") =>
        new(ResolveResult.Compatible(new ResolveCandidate(source, basis, source)),
            new EngineeringValue("x", value, unit, Now.AddSeconds(-ageSeconds), quality, "fake"), null);

    public static ResolvePreview Choose(params string[] sources) =>
        new(new ResolveResult(ResolveOutcome.NeedsSelection, sources.Select(s => new ResolveCandidate(s, ResolveBasis.Current, s)).ToList(), []), null, null);

    public static ResolvePreview Fail(ResolveOutcome outcome, string reason) => new(ResolveResult.Failed(outcome, reason), null, null);
}
