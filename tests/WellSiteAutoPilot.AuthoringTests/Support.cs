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

/// <summary>A provider double that answers per input name, so draft behaviour is tested without a live system.</summary>
internal sealed class FakeInputs(Dictionary<string, ResolvePreview> answers) : IAuthoringInputs
{
    public List<string> Asked { get; } = [];
    public (string SetPoint, decimal Delta)? Planned { get; private set; }

    public Task<ResolvePreview> ResolveAsync(string externalId, string input, ResolveBasis? basis, CancellationToken cancellationToken)
    {
        Asked.Add(input);
        if (answers.TryGetValue(input, out var answer)) return Task.FromResult(answer);
        return Task.FromResult(new ResolvePreview(ResolveResult.Failed(ResolveOutcome.NotApplicable, $"{input} is not offered on this asset."), null, null));
    }

    public Task<SetPointWritePlan> PlanSetPointAsync(string externalId, string setPoint, decimal delta, CancellationToken cancellationToken)
    {
        Planned = (setPoint, delta);
        return Task.FromResult(new SetPointWritePlan(externalId, setPoint, "Ideal PF", "%", 0, delta, 0, 100, true, null));
    }

    public static ResolvePreview Value(string source, decimal value, ResolveBasis basis = ResolveBasis.Current) =>
        new(ResolveResult.Compatible(new ResolveCandidate(source, basis, source)),
            new EngineeringValue("x", value, "psi", DateTimeOffset.UtcNow, "Good", "fake"), null);

    public static ResolvePreview Choose(params string[] sources) =>
        new(new ResolveResult(ResolveOutcome.NeedsSelection, sources.Select(s => new ResolveCandidate(s, ResolveBasis.Current, s)).ToList(), []), null, null);
}
