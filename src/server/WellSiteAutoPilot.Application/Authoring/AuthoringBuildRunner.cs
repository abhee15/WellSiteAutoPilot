using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace WellSiteAutoPilot.Application.Authoring;

/// <summary>Runs builds in the background, one at a time, and ignores results for superseded or cancelled builds.</summary>
public sealed class AuthoringBuildRunner(IAuthoringDraftStore store, IAuthoringBuilder builder, TimeProvider clock) : IDisposable
{
    private readonly ConcurrentDictionary<Guid, (Guid BuildId, CancellationTokenSource Cancellation)> running = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    public void Dispose() => gate.Dispose();

    public bool IsRunning(Guid draftId, Guid buildId) =>
        running.TryGetValue(draftId, out var entry) && entry.BuildId == buildId;

    public void Cancel(Guid draftId)
    {
        if (running.TryRemove(draftId, out var entry)) entry.Cancellation.Cancel();
    }

    public void Start(Guid draftId, Guid buildId, AuthoringGeneratedProject project)
    {
        Cancel(draftId);
        var cancellation = new CancellationTokenSource();
        running[draftId] = (buildId, cancellation);
        _ = Task.Run(() => RunAsync(draftId, buildId, project, cancellation), CancellationToken.None);
    }

    private async Task RunAsync(Guid draftId, Guid buildId, AuthoringGeneratedProject project,
        CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        AuthoringBuilderResult? result = null;
        var state = AuthoringBuildState.Failed;
        IReadOnlyList<AuthoringIssue> diagnostics = [];
        try
        {
            await gate.WaitAsync(token);
            try { result = await builder.BuildAsync(project, token); }
            finally { gate.Release(); }
            diagnostics = result.Diagnostics;
            if (result.Success && result.Package is not null)
            {
                await store.SavePackageAsync(draftId, buildId, result.Package, CancellationToken.None);
                state = AuthoringBuildState.Succeeded;
            }
        }
        catch (OperationCanceledException) { state = AuthoringBuildState.Cancelled; }
        catch (Exception error)
        {
            diagnostics = [new(AuthoringIssueSeverity.Error, "AUTHORING_BUILD_FAILED", "build",
                "The build failed unexpectedly: " + error.GetType().Name)];
        }
        var package = state == AuthoringBuildState.Succeeded ? result!.Package : null;
        await store.UpdateAsync(draftId, record =>
        {
            if (record.Build is not { State: AuthoringBuildState.Building } build || build.BuildId != buildId) return record;
            return record with
            {
                Build = build with
                {
                    State = state, Diagnostics = diagnostics, CompletedAt = clock.GetUtcNow(),
                    PackageSha256 = package is null ? null : Convert.ToHexStringLower(SHA256.HashData(package)),
                    PackageBytes = package?.LongLength ?? 0
                }
            };
        }, CancellationToken.None);
        if (running.TryGetValue(draftId, out var entry) && entry.BuildId == buildId)
        {
            running.TryRemove(draftId, out _);
            cancellation.Dispose();
        }
    }
}

