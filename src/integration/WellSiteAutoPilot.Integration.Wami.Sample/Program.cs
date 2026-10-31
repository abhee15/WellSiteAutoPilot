using WellSiteAutoPilot.Integration.Wami;
using WellSiteAutoPilot.Integration.Wami.Sample;

// Offline run against the deterministic fake WAMI (a model file argument is not used by the fake).
var fake = new FakeWamiClient();
var model = new InlineModelSource();
var run = await WellAnalysisScenario.RunAsync(fake, model, 300m, "{\"minimumRate\":250}");
WellAnalysisScenario.Print(run);
return run.Composition.Status == CompositionStatus.Completed && run.Module.Recommendations.Count == 1 ? 0 : 1;

internal sealed class InlineModelSource : IModelArtifactSource
{
    public ValueTask<ModelArtifact> GetCurrentModelAsync(string assetExternalId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ModelArtifact("fake-model", "1", "json", WellAnalysisScenario.FakeModel()));
}
