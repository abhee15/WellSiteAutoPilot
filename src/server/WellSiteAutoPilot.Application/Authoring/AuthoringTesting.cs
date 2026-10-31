using System.Globalization;
using System.Text.Json;

namespace WellSiteAutoPilot.Application.Authoring;

// Wire protocol between the server and the out-of-process authoring test host
// (tools/WellSiteAutoPilot.AuthoringTestHost). The host loads the built package with the production package host.
public sealed record AuthoringTestHostInput(
    string RequirementId, string Quantity, string? Unit, JsonElement Value, string? SchemaId);

public sealed record AuthoringTestHostCase(
    string Id, string AssetRole, string ParametersJson, IReadOnlyList<AuthoringTestHostInput> Inputs);

public sealed record AuthoringTestHostRequest(
    string ModuleId, string Version, string Sha256, Guid AssetId, IReadOnlyList<AuthoringTestHostCase> Cases);

public sealed record AuthoringTestHostOutput(string Id, string Quantity, string? Unit, JsonElement Value);

/// <summary>Kind is recommendation or control; Value is the suggested or requested value when present.</summary>
public sealed record AuthoringTestHostAction(string Kind, string Code, string? Command, decimal? Value);

/// <summary>Outcome is Completed, Failed (module or host reported a failure code) or Error (unexpected).</summary>
public sealed record AuthoringTestHostCaseResult(
    string Id, string Outcome, string? OutcomeCode, string? FailureCode, string? Message,
    IReadOnlyList<AuthoringTestHostOutput> Outputs, IReadOnlyDictionary<string, string> Trace,
    IReadOnlyList<AuthoringTestHostAction> Actions);

public sealed record AuthoringTestHostResult(IReadOnlyList<AuthoringTestHostCaseResult> Cases, string? Error);

public interface IAuthoringTestExecutor
{
    bool Available { get; }
    /// <summary>Runs the cases against the built package outside the server process.</summary>
    Task<AuthoringTestHostResult> RunAsync(byte[] package, AuthoringTestHostRequest request,
        CancellationToken cancellationToken);
}

public sealed record AuthoringTestValue(string Id, JsonElement Value, string? Unit);

public sealed record AuthoringTestCaseResult(
    string FixtureId, string Label, bool Passed, string Outcome, string? FailureCode, string? Message,
    IReadOnlyList<AuthoringTestValue> Outputs, IReadOnlyList<AuthoringTestValue> Intermediates,
    IReadOnlyList<string> Actions, IReadOnlyList<string> Mismatches);

/// <summary>A test run is pinned to the draft revision and the exact built package it exercised.</summary>
public sealed record AuthoringTestRecord(
    Guid TestId, int Revision, Guid BuildId, string PackageSha256, AuthoringTestState State,
    IReadOnlyList<AuthoringTestCaseResult> Cases, IReadOnlyList<AuthoringIssue> Diagnostics,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt);

public static class AuthoringTestPlanner
{
    public static AuthoringTestHostCase BuildCase(AuthoringDocument document, AuthoringFixture fixture)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in document.Parameters)
            parameters[parameter.Id] = fixture.Parameters?.GetValueOrDefault(parameter.Id) ?? parameter.DefaultValue;
        var inputs = document.Inputs.Select(input => new AuthoringTestHostInput(
            input.Id, input.Quantity, string.IsNullOrEmpty(input.Unit) ? null : input.Unit,
            fixture.Inputs[input.Id], input.ValueSchema?.SchemaId)).ToArray();
        return new(fixture.Id, document.AssetRoles[0].Role, JsonSerializer.Serialize(parameters), inputs);
    }

    public static AuthoringTestCaseResult Evaluate(AuthoringDocument document, AuthoringFixture fixture,
        AuthoringTestHostCaseResult run)
    {
        var mismatches = new List<string>();
        if (run.Outcome != "Completed")
            mismatches.Add($"The module did not complete ({run.FailureCode ?? run.Outcome}).");
        var outputs = run.Outputs.Select(o => new AuthoringTestValue(o.Id, o.Value, o.Unit)).ToArray();
        var actions = run.Actions.Select(a => a.Code).ToArray();
        if (run.Outcome == "Completed")
        {
            foreach (var (id, expected) in fixture.ExpectedOutputs ?? new Dictionary<string, JsonElement>())
            {
                var actual = outputs.FirstOrDefault(o => o.Id == id);
                if (actual is null) mismatches.Add($"Expected output {id} was not produced.");
                else if (!JsonEquivalent(expected, actual.Value))
                    mismatches.Add($"Output {id} was {actual.Value.GetRawText()} but {expected.GetRawText()} was expected.");
            }
            if (fixture.ExpectedActions is { } expectedActions)
            {
                var wanted = expectedActions.Select(ActionCode).Order(StringComparer.Ordinal).ToArray();
                var got = actions.Order(StringComparer.Ordinal).ToArray();
                if (!wanted.SequenceEqual(got))
                    mismatches.Add($"Actions fired [{string.Join(", ", got)}] but [{string.Join(", ", wanted)}] were expected.");
            }
        }
        var intermediates = run.Trace.OrderBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => new AuthoringTestValue(t.Key, JsonSerializer.SerializeToElement(t.Value), null)).ToArray();
        return new(fixture.Id, fixture.Label, mismatches.Count == 0, run.Outcome, run.FailureCode, run.Message,
            outputs, intermediates, actions, mismatches);
    }

    /// <summary>The intent code the generator emits for an action ID.</summary>
    public static string ActionCode(string actionId) => actionId.ToUpperInvariant().Replace('-', '_').Replace('.', '_');

    private static bool JsonEquivalent(JsonElement expected, JsonElement actual)
    {
        if (TryNumber(expected, out var left) && TryNumber(actual, out var right)) return left == right;
        return expected.ValueKind == actual.ValueKind && expected.GetRawText() == actual.GetRawText();
    }

    private static bool TryNumber(JsonElement value, out decimal number)
    {
        number = 0m;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetDecimal(out number),
            JsonValueKind.String => decimal.TryParse(value.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out number),
            _ => false
        };
    }
}
