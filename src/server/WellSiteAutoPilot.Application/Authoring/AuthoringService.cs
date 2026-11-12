using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Authoring;

/// <summary>Test a draft: against a connection and asset (live), or against typed sample values.</summary>
public sealed record EvaluateDraftRequest(
    ModuleDraft Draft, Guid? ConnectionId = null, string? ExternalId = null,
    Dictionary<string, string>? Samples = null, Dictionary<string, string>? Parameters = null);

/// <summary>What authoring needs from a connection: resolve one input on one asset, and dry-run a set point change.</summary>
public interface IAuthoringInputs
{
    Task<ResolvePreview> ResolveAsync(string externalId, string input, ResolveBasis? basis, CancellationToken cancellationToken);
    Task<SetPointWritePlan> PlanSetPointAsync(string externalId, string setPoint, decimal delta, CancellationToken cancellationToken);
}

/// <summary>Reads through a saved connection. Read-only: set point changes are planned, never sent.</summary>
public sealed class ConnectionAuthoringInputs(Connections.ConnectionResolveService resolver, Guid connectionId) : IAuthoringInputs
{
    public Task<ResolvePreview> ResolveAsync(string externalId, string input, ResolveBasis? basis, CancellationToken cancellationToken) =>
        resolver.ResolveAsync(connectionId, externalId, input, basis, true, cancellationToken);

    public Task<SetPointWritePlan> PlanSetPointAsync(string externalId, string setPoint, decimal delta, CancellationToken cancellationToken) =>
        resolver.PlanSetPointAsync(connectionId, externalId, setPoint, delta, cancellationToken);
}

/// <summary>
/// Tests a draft. Inputs come from the connection's live provider for one asset, or from typed sample values when
/// there is no connection. Either way the result carries a trace from each output back to the inputs that fed it.
/// </summary>
public static class AuthoringService
{
    public static async Task<ModuleRun> EvaluateAsync(
        ModuleDraft draft, IAuthoringInputs? inputs, string? externalId,
        IReadOnlyDictionary<string, string>? samples, IReadOnlyDictionary<string, string>? parameters,
        CancellationToken cancellationToken = default)
    {
        var analysis = DraftAnalyzer.Analyze(draft);
        if (!analysis.Valid)
            return DraftEvaluator.Evaluate(draft, [], parameters);

        var traces = new List<InputTrace>();
        foreach (var input in draft.Inputs)
        {
            if (inputs is not null && !string.IsNullOrWhiteSpace(externalId))
                traces.Add(await ResolveInputAsync(input, inputs, externalId, cancellationToken));
            else
                traces.Add(FromSample(input, samples));
        }

        var run = DraftEvaluator.Evaluate(draft, traces, parameters);
        if (inputs is null || string.IsNullOrWhiteSpace(externalId) || run.Outcome != ModuleOutcome.Runs)
            return run;

        var outputs = new List<OutputResult>();
        foreach (var output in run.Outputs)
        {
            if (output.Kind != "setpoint" || output.Value.Kind != ValueKind.Number)
            {
                outputs.Add(output);
                continue;
            }
            try
            {
                var plan = await inputs.PlanSetPointAsync(externalId, output.Target!, (decimal)output.Value.Number!.Value, cancellationToken);
                outputs.Add(output with { Plan = plan, Note = plan.Allowed ? "Dry run only. Nothing was sent." : plan.Refusal });
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                outputs.Add(output with { Note = "The set point change could not be planned: " + error.Message });
            }
        }
        return run with { Outputs = outputs };
    }

    private static InputTrace FromSample(DraftInput input, IReadOnlyDictionary<string, string>? samples)
    {
        if (samples is not null && samples.TryGetValue(input.Name, out var text) && !string.IsNullOrWhiteSpace(text))
            return new InputTrace(input.Name, "Sample", Val.Parse(text), null, "sample", null, null, [], [], input.Required);
        var reason = "No sample value was given. Type one, or choose a connection and an asset.";
        return new InputTrace(input.Name, "CannotDetermine", Val.Missing(reason), null, null, null, null, [reason], [], input.Required);
    }

    private static async Task<InputTrace> ResolveInputAsync(DraftInput input, IAuthoringInputs inputs, string externalId, CancellationToken cancellationToken)
    {
        var reasons = new List<string>();
        var candidates = new List<CandidateInfo>();
        var worst = "NotApplicable";
        var needsSelection = false;
        foreach (var source in input.Sources)
        {
            ResolveBasis? basis = Enum.TryParse<ResolveBasis>(source.Basis, out var parsed) ? parsed : null;
            ResolvePreview preview;
            try
            {
                preview = await inputs.ResolveAsync(externalId, source.Input, basis, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                reasons.Add($"{source.Input}: {error.Message}");
                if (!needsSelection) worst = "CannotDetermine";
                continue;
            }

            var result = preview.Result;
            if (result.Outcome == ResolveOutcome.Compatible)
            {
                var candidate = result.Candidates.Count > 0 ? result.Candidates[0] : null;
                if (preview.Value?.Value is { } number)
                    return new InputTrace(input.Name, "Resolved", Val.Of((double)number), preview.Value.Unit, preview.Value.Source,
                        candidate?.Basis.ToString(), candidate?.Source ?? source.Input, reasons, [], input.Required);
                reasons.Add($"{source.Input}: {preview.ReadProblem ?? "resolved but returned no value"}");
                if (!needsSelection) worst = "CannotDetermine";
                continue;
            }
            if (result.Outcome == ResolveOutcome.NeedsSelection)
            {
                needsSelection = true;
                worst = "NeedsSelection";
                candidates.AddRange(result.Candidates.Select(c => new CandidateInfo(c.Source, c.Basis.ToString(), c.Description)));
                reasons.Add($"{source.Input}: choose which value to use.");
                continue;
            }
            reasons.AddRange(result.Reasons.Select(r => $"{source.Input}: {r}"));
            if (!needsSelection && worst != "CannotDetermine")
                worst = result.Outcome == ResolveOutcome.CannotDetermine ? "CannotDetermine" : result.Outcome.ToString();
        }
        return new InputTrace(input.Name, worst, Val.Missing(reasons.FirstOrDefault() ?? "no source answered"),
            null, null, null, null, reasons, candidates, input.Required);
    }
}
