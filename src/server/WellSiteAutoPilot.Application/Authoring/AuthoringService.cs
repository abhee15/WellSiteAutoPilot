using WellSiteAutoPilot.Integration.Contracts.Providers;

namespace WellSiteAutoPilot.Application.Authoring;

/// <summary>Test a draft: against a connection and asset (live), or against typed sample values.</summary>
public sealed record EvaluateDraftRequest(
    ModuleDraft Draft, Guid? ConnectionId = null, string? ExternalId = null,
    Dictionary<string, string>? Samples = null, Dictionary<string, string>? Parameters = null, ModuleBindings? Bindings = null);

/// <summary>What authoring needs from a connection: resolve one input on one asset, and dry-run a set point change.</summary>
public interface IAuthoringInputs
{
    Task<ResolvePreview> ResolveAsync(string externalId, string input, ResolveBasis? basis, CancellationToken cancellationToken);
    Task<SetPointWritePlan> PlanSetPointAsync(string externalId, string setPoint, decimal proposed, CancellationToken cancellationToken);
}

/// <summary>Reads through a saved connection. Read-only: set point changes are planned, never sent.</summary>
public sealed class ConnectionAuthoringInputs(Connections.ConnectionResolveService resolver, Guid connectionId) : IAuthoringInputs
{
    public Task<ResolvePreview> ResolveAsync(string externalId, string input, ResolveBasis? basis, CancellationToken cancellationToken) =>
        resolver.ResolveAsync(connectionId, externalId, input, basis, true, cancellationToken);

    public Task<SetPointWritePlan> PlanSetPointAsync(string externalId, string setPoint, decimal proposed, CancellationToken cancellationToken) =>
        resolver.PlanSetPointAsync(connectionId, externalId, setPoint, 0m, proposed, cancellationToken);
}

/// <summary>
/// Tests a draft. Inputs come from the connection's live provider for one asset, or from typed sample values when
/// there is no connection. A source that is not applicable passes the input on to the next binding; a failure, a
/// choice the author must make, or a reading that is too old stops the input, so a broken primary is never hidden.
/// </summary>
public static class AuthoringService
{
    public static async Task<ModuleRun> EvaluateAsync(
        ModuleDraft draft, IAuthoringInputs? inputs, string? externalId,
        IReadOnlyDictionary<string, string>? samples, IReadOnlyDictionary<string, string>? parameters,
        ModuleBindings? bindings = null, TimeProvider? clock = null, CancellationToken cancellationToken = default)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var live = inputs is not null && !string.IsNullOrWhiteSpace(externalId);
        var analysis = DraftAnalyzer.Analyze(draft);
        if (!analysis.Valid)
            return DraftEvaluator.Evaluate(draft, [], parameters, now);

        var traces = new List<InputTrace>();
        foreach (var input in draft.Inputs)
            traces.Add(live ? await ResolveInputAsync(input, bindings, inputs!, externalId!, now, cancellationToken) : FromSample(input, samples));

        var run = DraftEvaluator.Evaluate(draft, traces, parameters, now) with { Mode = live ? "live" : "sample", ExternalId = live ? externalId : null };
        if (!live)
            return run with { Outputs = run.Outputs.Select(NoController).ToList() };
        if (run.Outcome != ModuleOutcome.Runs)
            return run;

        var outputs = new List<OutputResult>();
        foreach (var output in run.Outputs)
        {
            var spec = draft.Outputs.First(o => o.Name == output.Name);
            outputs.Add(output.Kind == "setpoint" && output.Proposed == true
                ? await PlanAsync(output, spec, bindings, inputs!, externalId!, cancellationToken)
                : output);
        }
        return run with { Outputs = outputs };
    }

    private static OutputResult NoController(OutputResult output) =>
        output.Kind != "setpoint" ? output
            : output with { Checks = [.. output.Checks ?? [], new ProposalCheck("Controller", "Info", "No controller is connected, so its range and step limit were not checked.")] };

    private static async Task<OutputResult> PlanAsync(
        OutputResult output, DraftOutput spec, ModuleBindings? bindings, IAuthoringInputs inputs, string externalId, CancellationToken cancellationToken)
    {
        var target = bindings?.Targets is { } map && map.TryGetValue(spec.Name, out var bound) && !string.IsNullOrWhiteSpace(bound) ? bound : spec.Target!;
        try
        {
            var number = output.Value.Number!.Value;
            var plan = await inputs.PlanSetPointAsync(externalId, target, number, cancellationToken);
            var module = Units.Normalize(spec.Unit);
            var controller = Units.Normalize(plan.Unit);
            var notes = new List<ProposalCheck>();
            if (module is not null && controller is not null && module != controller)
            {
                if (!Units.TryConvert(number, module, controller, out var converted, out var why))
                    return output with
                    {
                        Proposed = false, Note = "Not proposed. Nothing was sent.",
                        Checks = [.. output.Checks ?? [], new ProposalCheck("Unit", "Fail", $"The module proposes {module} but the controller uses {controller}: {why}")]
                    };
                plan = await inputs.PlanSetPointAsync(externalId, target, converted, cancellationToken);
                notes.Add(new("Unit", "Info", $"Converted from {module} to the controller's {controller}."));
            }
            else if (module is null && controller is not null)
                notes.Add(new("Unit", "Info", $"The output declares no unit, so the value was taken to be in the controller's {controller}."));
            var completed = DraftEvaluator.AfterPlan(output, spec, plan);
            return completed with { Checks = [.. completed.Checks!, .. notes] };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return output with
            {
                Proposed = false, Note = "Not proposed. Nothing was sent.",
                Checks = [.. output.Checks ?? [], new ProposalCheck("Controller", "Fail", "The set point could not be checked against the controller: " + error.Message)]
            };
        }
    }

    internal static InputTrace FromSample(DraftInput input, IReadOnlyDictionary<string, string>? samples)
    {
        if (samples is not null && samples.TryGetValue(input.Name, out var text) && !string.IsNullOrWhiteSpace(text))
        {
            var typed = Val.Parse(text, input.Unit);
            if (typed.Kind == ValueKind.Number && (input.ValidMin is { } low && typed.Number < low || input.ValidMax is { } high && typed.Number > high))
            {
                var outside = $"The value {Units.Format(typed.Number!.Value)} is outside the range this input accepts ({Units.Format(input.ValidMin, "no minimum")} to {Units.Format(input.ValidMax, "no maximum")}), so it is not trusted.";
                return new InputTrace(input.Name, "InvalidValue", Val.Missing(outside), Units.Normalize(input.Unit), "sample", null, null, [outside], [], input.Required,
                    Quantity: input.Quantity);
            }
            return new InputTrace(input.Name, "Sample", typed, Units.Normalize(input.Unit), "sample", null, null, [], [], input.Required,
                Quantity: input.Quantity);
        }
        var reason = "No sample value was given. Type one, or choose a connection and an asset.";
        return new InputTrace(input.Name, "CannotDetermine", Val.Missing(reason), null, null, null, null, [reason], [], input.Required, Quantity: input.Quantity);
    }

    private static async Task<InputTrace> ResolveInputAsync(
        DraftInput input, ModuleBindings? bindings, IAuthoringInputs inputs, string externalId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sources = bindings?.Inputs is { } map && map.TryGetValue(input.Name, out var bound) && bound.Count > 0
            ? bound : [new BindingSource(input.Quantity, input.Basis)];
        var reasons = new List<string>();

        InputTrace Stop(string resolution, string? usedInput = null, IReadOnlyList<CandidateInfo>? candidates = null) =>
            new(input.Name, resolution, Val.Missing(reasons.FirstOrDefault() ?? "no source answered"), null, null, null, usedInput,
                reasons, candidates ?? [], input.Required, MaxAgeSeconds: input.MaxAgeSeconds, Quantity: input.Quantity);

        foreach (var source in sources)
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
                return Stop("CannotDetermine", source.Input);
            }

            var result = preview.Result;
            switch (result.Outcome)
            {
                case ResolveOutcome.NotApplicable:
                    reasons.AddRange(result.Reasons.Select(r => $"{source.Input}: {r}"));
                    continue;
                case ResolveOutcome.NeedsSelection:
                    reasons.Add($"{source.Input}: choose which value to use.");
                    return Stop("NeedsSelection", source.Input, result.Candidates.Select(c => new CandidateInfo(c.Source, c.Basis.ToString(), c.Description)).ToList());
                case ResolveOutcome.Compatible:
                    break;
                default:
                    reasons.AddRange(result.Reasons.Select(r => $"{source.Input}: {r}"));
                    return Stop(result.Outcome == ResolveOutcome.Misconfigured ? "Misconfigured" : "CannotDetermine", source.Input);
            }

            var candidate = result.Candidates.Count > 0 ? result.Candidates[0] : null;
            if (preview.Value is not { Value: { } number } reading)
            {
                reasons.Add($"{source.Input}: {preview.ReadProblem ?? "resolved but returned no value"}");
                return Stop("CannotDetermine", source.Input);
            }

            var accepted = InputAcceptance.Accept(input, number, reading.Unit, reading.TimestampUtc, reading.Quality, now);
            if (accepted.Reason is not null) reasons.Add($"{source.Input}: {accepted.Reason}");
            return new InputTrace(input.Name, accepted.Resolution, accepted.Value, accepted.Value.Unit, reading.Source,
                candidate?.Basis.ToString(), candidate?.Source ?? source.Input, reasons, [], input.Required,
                accepted.AgeSeconds, input.MaxAgeSeconds, reading.TimestampKind, input.Quantity);
        }
        return Stop("NotApplicable");
    }
}
