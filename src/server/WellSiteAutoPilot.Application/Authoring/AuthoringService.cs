using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Application.Authoring;

public sealed class AuthoringService(
    AuthoringOptions options, IAuthoringDraftStore store, AuthoringBuildRunner runner,
    LogicModulePackageService packages, IAuthoringTestExecutor executor, TimeProvider clock)
{
    public bool Enabled => options.Enabled;

    private void RequireEnabled()
    {
        if (!options.Enabled)
            throw new WellSiteAutoPilotException("AUTHORING_LOCAL_DISABLED", FailureKind.NotFound,
                "Local authoring is disabled. Set Authoring:LocalBuild:Enabled to true for local development.");
    }

    public async Task<IReadOnlyList<AuthoringDraftView>> ListAsync(CancellationToken cancellationToken)
    {
        RequireEnabled();
        var drafts = await store.ListAsync(cancellationToken);
        return [.. drafts.OrderByDescending(d => d.UpdatedAt).Select(View)];
    }

    public async Task<AuthoringDraftView> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireEnabled();
        return View(await LoadAsync(id, cancellationToken));
    }

    public async Task<AuthoringDraftView> CreateAsync(string? starterId, JsonElement? document, string createdBy,
        CancellationToken cancellationToken)
    {
        RequireEnabled();
        AuthoringDocument source;
        if (document is { ValueKind: JsonValueKind.Object } supplied) source = AuthoringJson.Read(supplied);
        else
        {
            var starter = AuthoringStarters.Find(starterId ?? AuthoringStarters.ObserveInputId)
                ?? throw new WellSiteAutoPilotException("AUTHORING_STARTER_UNKNOWN", FailureKind.Validation,
                    "The starter is not recognized.");
            source = starter.Document;
        }
        var now = clock.GetUtcNow();
        var record = new AuthoringDraftRecord(Guid.NewGuid(), 1, AuthoringJson.Digest(source), source, now, now, createdBy);
        await store.CreateAsync(record, cancellationToken);
        return View(record);
    }

    public async Task<AuthoringDraftView> UpdateAsync(Guid id, int expectedRevision, JsonElement document,
        CancellationToken cancellationToken)
    {
        RequireEnabled();
        var next = AuthoringJson.Read(document);
        var digest = AuthoringJson.Digest(next);
        var changed = false;
        var updated = await store.UpdateAsync(id, record =>
        {
            if (record.Revision != expectedRevision)
                throw new WellSiteAutoPilotException("AUTHORING_REVISION_CONFLICT", FailureKind.Conflict,
                    "The draft changed since it was loaded. Reload and reapply the edit.");
            if (record.ContentDigest == digest) return record;
            changed = true;
            return record with
            {
                Revision = record.Revision + 1, ContentDigest = digest, Document = next, UpdatedAt = clock.GetUtcNow()
            };
        }, cancellationToken) ?? throw NotFound();
        if (changed) runner.Cancel(id);
        if (changed && updated.Build is { State: AuthoringBuildState.Building } building)
            updated = await store.UpdateAsync(id, r => r.Build?.BuildId == building.BuildId
                ? r with { Build = building with { State = AuthoringBuildState.Cancelled, CompletedAt = clock.GetUtcNow() } }
                : r, cancellationToken) ?? updated;
        return View(updated);
    }

    public async Task<AuthoringDraftView> ValidateAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireEnabled();
        var draft = await LoadAsync(id, cancellationToken);
        var report = await ValidateDocumentAsync(draft.Document, cancellationToken);
        var validation = new AuthoringValidationRecord(draft.Revision, report.IsValid, report.Issues, clock.GetUtcNow());
        var saved = await store.UpdateAsync(id, record => record.Revision == validation.Revision
            ? record with { Validation = validation } : record, cancellationToken) ?? throw NotFound();
        return View(saved);
    }

    /// <summary>Static document rules plus the platform manifest reader over the generated manifest.</summary>
    public static async Task<AuthoringValidationReport> ValidateDocumentAsync(
        AuthoringDocument document, CancellationToken cancellationToken)
    {
        var report = AuthoringDraftValidator.Validate(document);
        if (!report.IsValid) return report;
        var issues = report.Issues.ToList();
        try
        {
            var manifest = AuthoringModuleGenerator.GenerateManifest(document);
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(manifest));
            await LogicModuleManifestReader.ReadAsync(stream, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            var message = error is WellSiteAutoPilotException failure ? failure.SafeDetail : error.Message;
            issues.Add(new(AuthoringIssueSeverity.Error, "AUTHORING_MANIFEST_INVALID", "module",
                "The generated module manifest was rejected: " + message));
        }
        return report with { IsValid = issues.All(i => i.Severity != AuthoringIssueSeverity.Error), Issues = issues };
    }

    public async Task<AuthoringDraftView> BuildAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireEnabled();
        var draft = await LoadAsync(id, cancellationToken);
        var report = await ValidateDocumentAsync(draft.Document, cancellationToken);
        var validation = new AuthoringValidationRecord(draft.Revision, report.IsValid, report.Issues, clock.GetUtcNow());
        if (!report.IsValid)
        {
            await store.UpdateAsync(id, r => r.Revision == validation.Revision ? r with { Validation = validation } : r,
                cancellationToken);
            throw new WellSiteAutoPilotException("AUTHORING_DRAFT_INVALID", FailureKind.Validation,
                "The draft has validation errors. Fix them before building.");
        }
        var project = AuthoringModuleGenerator.Generate(draft.Document);
        var buildId = Guid.NewGuid();
        var build = new AuthoringBuildRecord(buildId, draft.Revision, draft.ContentDigest,
            AuthoringBuildState.Building, [], clock.GetUtcNow());
        var saved = await store.UpdateAsync(id, record =>
        {
            if (record.Revision != draft.Revision)
                throw new WellSiteAutoPilotException("AUTHORING_REVISION_CONFLICT", FailureKind.Conflict,
                    "The draft changed while the build was starting.");
            if (record.Build is { State: AuthoringBuildState.Building } current && runner.IsRunning(id, current.BuildId))
                throw new WellSiteAutoPilotException("AUTHORING_BUILD_IN_PROGRESS", FailureKind.Conflict,
                    "A build is already running for this draft.");
            return record with { Validation = validation, Build = build };
        }, cancellationToken) ?? throw NotFound();
        runner.Start(id, buildId, project);
        return View(saved);
    }

    public async Task<AuthoringDraftView> CancelBuildAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireEnabled();
        var draft = await LoadAsync(id, cancellationToken);
        runner.Cancel(id);
        if (draft.Build is not { State: AuthoringBuildState.Building } build) return View(draft);
        var saved = await store.UpdateAsync(id, r => r.Build?.BuildId == build.BuildId && r.Build.State == AuthoringBuildState.Building
            ? r with { Build = build with { State = AuthoringBuildState.Cancelled, CompletedAt = clock.GetUtcNow() } } : r,
            cancellationToken) ?? throw NotFound();
        return View(saved);
    }

    public async Task<(byte[] Package, string FileName, string Sha256)> DownloadAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireEnabled();
        var (draft, build, package) = await LoadCurrentPackageAsync(id, cancellationToken);
        var module = draft.Document.Module;
        return (package, $"{module.ModuleId}-{module.Version}.wsamodule", build.PackageSha256!);
    }

    /// <summary>Hands the exact stored artifact to the normal package upload: untrusted and disabled, never approved.</summary>
    public async Task<InstalledLogicModule> AddToCatalogAsync(Guid id, AccessSubject subject, SecurityActorContext actor,
        CancellationToken cancellationToken)
    {
        RequireEnabled();
        var (_, build, package) = await LoadCurrentPackageAsync(id, cancellationToken);
        await using var stream = new MemoryStream(package, writable: false);
        var installed = await packages.UploadAsync(stream, subject, actor, cancellationToken);
        var submission = new AuthoringCatalogSubmission(installed.Id, clock.GetUtcNow());
        await store.UpdateAsync(id, r => r.Build?.BuildId == build.BuildId
            ? r with { Build = build with { Catalog = submission } } : r, CancellationToken.None);
        return installed;
    }

    /// <summary>Runs the draft's fixtures against the built package in the out-of-process test host.</summary>
    public async Task<AuthoringDraftView> TestAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireEnabled();
        if (!executor.Available)
            throw new WellSiteAutoPilotException("AUTHORING_TEST_HOST_UNAVAILABLE", FailureKind.DependencyUnavailable,
                "The authoring test host was not found. Build the solution or set Authoring:LocalBuild:TestHostPath.");
        var (draft, build, package) = await LoadCurrentPackageAsync(id, cancellationToken);
        var fixtures = draft.Document.Fixtures ?? [];
        if (fixtures.Count == 0)
            throw new WellSiteAutoPilotException("AUTHORING_FIXTURE_REQUIRED", FailureKind.Validation,
                "Add at least one sample fixture to the draft before running tests.");
        var started = clock.GetUtcNow();
        var request = new AuthoringTestHostRequest(draft.Document.Module.ModuleId, draft.Document.Module.Version,
            build.PackageSha256!, Guid.Parse("00000000-0000-0000-0000-0000000000a1"),
            [.. fixtures.Select(f => AuthoringTestPlanner.BuildCase(draft.Document, f))]);
        var diagnostics = new List<AuthoringIssue>();
        var cases = new List<AuthoringTestCaseResult>();
        var state = AuthoringTestState.Passed;
        var result = await executor.RunAsync(package, request, cancellationToken);
        if (result.Error is { } error)
        {
            state = AuthoringTestState.Failed;
            diagnostics.Add(new(AuthoringIssueSeverity.Error, "AUTHORING_TEST_HOST_FAILED", "test", error));
        }
        foreach (var fixture in fixtures)
        {
            var run = result.Cases.FirstOrDefault(c => c.Id == fixture.Id);
            if (run is null) { state = AuthoringTestState.Failed; continue; }
            var evaluated = AuthoringTestPlanner.Evaluate(draft.Document, fixture, run);
            if (!evaluated.Passed) state = AuthoringTestState.Failed;
            cases.Add(evaluated);
        }
        var record = new AuthoringTestRecord(Guid.NewGuid(), draft.Revision, build.BuildId, build.PackageSha256!,
            state, cases, diagnostics, started, clock.GetUtcNow());
        var saved = await store.UpdateAsync(id, r => r.Revision == record.Revision && r.Build?.BuildId == build.BuildId
            ? r with { Test = record } : r, cancellationToken) ?? throw NotFound();
        return View(saved);
    }

    private async Task<(AuthoringDraftRecord Draft, AuthoringBuildRecord Build, byte[] Package)> LoadCurrentPackageAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var draft = await LoadAsync(id, cancellationToken);
        var view = View(draft);
        if (view.Build != AuthoringBuildState.Succeeded || draft.Build is not { } build)
            throw new WellSiteAutoPilotException("AUTHORING_BUILD_NOT_CURRENT", FailureKind.Conflict,
                "There is no successful build for the current draft revision. Build the draft first.");
        var package = await store.ReadPackageAsync(id, build.BuildId, cancellationToken);
        if (package is null || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(package)), build.PackageSha256,
                StringComparison.Ordinal))
            throw new WellSiteAutoPilotException("AUTHORING_ARTIFACT_INVALID", FailureKind.Conflict,
                "The stored build artifact is missing or does not match its recorded hash. Build again.");
        return (draft, build, package);
    }

    private async Task<AuthoringDraftRecord> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await store.GetAsync(id, cancellationToken) ?? throw NotFound();

    private static WellSiteAutoPilotException NotFound() =>
        new("AUTHORING_DRAFT_NOT_FOUND", FailureKind.NotFound, "The authoring draft was not found.");

    public AuthoringDraftView View(AuthoringDraftRecord draft)
    {
        var validation = draft.Validation switch
        {
            null => AuthoringValidationState.NotRun,
            { } v when v.Revision != draft.Revision => AuthoringValidationState.Stale,
            { IsValid: true } => AuthoringValidationState.Valid,
            _ => AuthoringValidationState.Invalid
        };
        var build = draft.Build;
        var buildState = AuthoringBuildState.None;
        if (build is not null)
        {
            if (build.State == AuthoringBuildState.Building && !runner.IsRunning(draft.Id, build.BuildId))
            {
                build = build with
                {
                    State = AuthoringBuildState.Failed,
                    Diagnostics = [new(AuthoringIssueSeverity.Error, "AUTHORING_BUILD_INTERRUPTED", "build",
                        "The build did not finish, for example because the server restarted. Build again.")]
                };
            }
            buildState = build.Revision != draft.Revision ? AuthoringBuildState.Stale : build.State;
        }
        var test = draft.Test;
        var testState = test is null ? AuthoringTestState.NotRun
            : test.Revision != draft.Revision || test.BuildId != draft.Build?.BuildId ? AuthoringTestState.Stale
            : test.State;
        return new(draft, validation, testState, buildState, build, test);
    }
}
