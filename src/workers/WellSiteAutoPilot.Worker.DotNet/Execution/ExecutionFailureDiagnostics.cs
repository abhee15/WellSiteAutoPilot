using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Integration.Contracts.Providers;
using WellSiteAutoPilot.Messaging.Contracts.Execution;

namespace WellSiteAutoPilot.Worker.DotNet.Execution;

internal sealed class ExecutionFailureDiagnostics
{
    private const string Key = "WSA.ExecutionInputDiagnostics.V1";
    private readonly object _gate = new();
    private readonly List<ExecutionInputDiagnosticV1> _items = [];

    public void Record(ExecutionInputBindingV2 input, EngineeringValue? value, string outcome)
    {
        lock (_gate)
        {
            if (_items.Count >= 64) return;
            _items.Add(new(input.RequirementId, input.AssetId, input.Source?.ConnectionId, input.Quantity,
                input.CanonicalUnit is { Length: <= 32 } unit && value?.Unit == unit ? unit : null,
                value?.Value, value?.TimestampUtc.ToUniversalTime(), value?.RetrievedAtUtc?.ToUniversalTime(),
                value?.TimestampKind == "UnverifiedScan" ? "UnverifiedScan" : null,
                value?.Quality is "Good" or "Uncertain" or "Bad" ? value.Quality : "Unknown", outcome));
        }
    }
    public void Attach(Exception error)
    {
        lock (_gate) error.Data[Key] = _items.ToArray();
    }
    public static IReadOnlyCollection<ExecutionInputDiagnosticV1>? Read(Exception error) =>
        error.Data[Key] as ExecutionInputDiagnosticV1[];
    public static string FailureCode(Exception error) => FailureClassifier.Classify(error).Code;
}
