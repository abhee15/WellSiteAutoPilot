using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Messaging.Contracts.Execution;
using WellSiteAutoPilot.ModuleSdk;
using WellSiteAutoPilot.Worker.DotNet.Execution;

internal static class ModuleResultFailureChecks
{
    public static async Task RunAsync(
        ExecutionRequestedV2 request, GatewayDataClient dataClient, DateTimeOffset now)
    {
        // A successful outcome is algorithm-defined. Never infer failure from its name.
        var successful = new LogicModuleExecutionResult("NO_ACTION_REQUIRED", [], [], [],
            new Dictionary<string, string>());
        var engine = new ModuleExecutionEngine(dataClient, new ResultResolver(successful));
        if ((await engine.ExecuteAsync(request, "successful-no-action", now)).OutcomeCode != "NO_ACTION_REQUIRED")
            throw new InvalidOperationException("A successful no-action result was rejected.");

        var legacy = new ExecutionRequestedV1(request.ExecutionId, request.ConfiguredLogicId,
            request.ModuleId, request.ModuleVersion, request.ConfigurationRevisionId,
            request.Assets.First().AssetId, "WELL-101", "PumpFillage", "Shadow", now);
        if ((await engine.ExecuteAsync(legacy, "legacy-success", now)).FailureCode is not null)
            throw new InvalidOperationException("Legacy result without FailureCode was rejected.");

        var failed = successful with
        {
            // Even a success-looking outcome and populated outputs cannot override failure.
            OutcomeCode = "LOOKS_COMPLETED",
            FailureCode = "MODULE_INPUT_REJECTED",
            EngineeringResults = [new EngineeringResultValue("ignore", request.Assets.First().AssetId, "PumpFillage", 99m, "%")],
            Recommendations = [new RecommendationIntentDraft("ignore", request.Assets.First().AssetId, "ignore", "ignore")],
            ControlIntents = [new ControlIntentDraft("ignore", request.Assets.First().AssetId, "ignore", null, null, null, "ignore")],
            StateUpdates = new Dictionary<string, string> { ["ignore"] = "ignore" }
        };
        var rejected = new ModuleExecutionEngine(dataClient, new ResultResolver(failed));
        await ExpectFailureAsync(() => rejected.ExecuteAsync(request, "module-failure-v2", now), "MODULE_INPUT_REJECTED");
        await ExpectFailureAsync(() => rejected.ExecuteAsync(legacy, "module-failure-v1", now), "MODULE_INPUT_REJECTED");
        await ExpectFailureAsync(async () =>
        {
            var observed = await rejected.ExecuteObservedAsync(request, "module-failure-observed", now);
            return observed.Result;
        }, "MODULE_INPUT_REJECTED");

        foreach (var invalidCode in new[] { "", " ", "lowercase", "9INVALID", "BAD-CODE", "BAD\nCODE", "ÅBC", new string('A', 129) })
        {
            var invalid = new ModuleExecutionEngine(dataClient, new ResultResolver(failed with { FailureCode = invalidCode }));
            await ExpectFailureAsync(() => invalid.ExecuteAsync(request, "invalid-module-code", now), "MODULE_RESULT_INVALID");
        }

        foreach (var validCode in new[] { "A", "MODULE_ERROR_42", new string('A', 128) })
        {
            var valid = new ModuleExecutionEngine(dataClient, new ResultResolver(failed with { FailureCode = validCode }));
            await ExpectFailureAsync(() => valid.ExecuteAsync(request, "valid-module-code", now), validCode);
        }

        var noResult = new ModuleExecutionEngine(dataClient, new ResultResolver(null!));
        await ExpectFailureAsync(() => noResult.ExecuteAsync(request, "null-module-result", now), "MODULE_RESULT_INVALID");
    }

    private static async Task ExpectFailureAsync(Func<Task<LogicModuleExecutionResult>> execute, string expected)
    {
        try
        {
            await execute();
            throw new InvalidOperationException("Failed module evaluation escaped as a completed result.");
        }
        catch (WellSiteAutoPilotException exception) when (exception.Code == expected)
        {
            var classified = FailureClassifier.Classify(exception);
            if (classified.Kind != FailureKind.Validation || classified.Retryable)
                throw new InvalidOperationException("Module evaluation failure classification drifted.");
        }
    }

    private sealed class ResultResolver(LogicModuleExecutionResult result) : ILogicModuleResolver
    {
        public ILogicModuleV1 Resolve(string moduleId, string version) =>
            new ResultModule(new LogicModuleIdentity(moduleId, version), result);
    }

    private sealed class ResultModule(LogicModuleIdentity identity, LogicModuleExecutionResult result) : ILogicModuleV1
    {
        public LogicModuleIdentity Identity { get; } = identity;

        public ValueTask<LogicModuleExecutionResult> ExecuteAsync(
            LogicModuleExecutionContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(result);
        }
    }
}
