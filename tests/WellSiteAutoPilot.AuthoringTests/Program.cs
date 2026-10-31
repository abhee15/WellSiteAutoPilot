using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WellSiteAutoPilot.Application.Authoring;
using WellSiteAutoPilot.Application.Logic;
using WellSiteAutoPilot.Application.Security;
using WellSiteAutoPilot.Domain.Logic;
using WellSiteAutoPilot.Failures;
using WellSiteAutoPilot.Infrastructure.Authoring;
using WellSiteAutoPilot.ModuleSdk;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("Authoring check failed: " + message);
}

static async Task<T> Throws<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); }
    catch (T error) { return error; }
    throw new InvalidOperationException("Authoring check failed: expected failure - " + message);
}

static AuthoringDocument Starter(string id) => AuthoringStarters.Find(id)!.Document;

static bool HasCode(AuthoringValidationReport report, string code) =>
    report.Issues.Any(i => i.Code == code && i.Severity == AuthoringIssueSeverity.Error);

// Static validation.
foreach (var starter in AuthoringStarters.All)
{
    var report = await AuthoringService.ValidateDocumentAsync(starter.Document, CancellationToken.None);
    Check(report.IsValid, $"starter {starter.Id} must validate: " + string.Join("; ", report.Issues.Select(i => i.Code + " " + i.Message)));
}

var threshold = Starter(AuthoringStarters.ThresholdAlertId);
Check(AuthoringDraftValidator.Validate(threshold).StepOrder.SequenceEqual(["shortfall", "fillage-ratio"]), "topological order is stable");

var cyclic = threshold with
{
    Steps =
    [
        new("a", "A", "calculation", "add", [new("step", "b"), new("literal", null, null, "1")]),
        new("b", "B", "calculation", "add", [new("step", "a"), new("literal", null, null, "1")])
    ],
    Outputs = [new("out", "Out", "Q", "%", new("step", "a"))],
    Conditions = [], Actions = []
};
Check(HasCode(AuthoringDraftValidator.Validate(cyclic), "AUTHORING_CYCLE"), "cycles are rejected");

var unitMismatch = threshold with
{
    Steps = [new("bad", "Bad", "calculation", "add", [new("input", "pump-fillage"), new("literal", "1")])],
    Conditions = [], Actions = [],
    Outputs = [new("out", "Out", "Q", "%", new("step", "bad"))]
};
// Adding a literal (no unit) is allowed to be flagged only when units conflict; a unit-mismatched add is the real case.
var mismatched = threshold with
{
    Parameters = [new("low-threshold", "Low", "40", "psi")],
    Steps = [new("bad", "Bad", "calculation", "add", [new("input", "pump-fillage"), new("parameter", "low-threshold")])],
    Conditions = [], Actions = [],
    Outputs = [new("out", "Out", "Q", "%", new("step", "bad"))]
};
Check(HasCode(AuthoringDraftValidator.Validate(mismatched), "AUTHORING_UNIT_MISMATCH"), "unit mismatch is rejected");
_ = unitMismatch;

Check(HasCode(AuthoringDraftValidator.Validate(threshold with { Parameters = [new("low-threshold", "Low", "4e1", "%")] }),
    "AUTHORING_DECIMAL_INVALID"), "non-canonical decimals are rejected");
Check(HasCode(AuthoringDraftValidator.Validate(threshold with
{
    Steps = [new("s", "S", "calculation", "divide", [new("input", "pump-fillage"), new("literal", "0")])],
    Conditions = [], Actions = [], Outputs = [new("o", "O", "Q", "1", new("step", "s"))]
}), "AUTHORING_DECIMAL_INVALID") || AuthoringDraftValidator.Validate(threshold with
{
    Steps = [new("s", "S", "calculation", "divide", [new("input", "pump-fillage"), new("literal", "0")])],
    Conditions = [], Actions = [], Outputs = [new("o", "O", "Q", "1", new("step", "s"))]
}).Issues.Any(i => i.Message.Contains("zero", StringComparison.OrdinalIgnoreCase)), "divide by literal zero is rejected");
Check(HasCode(AuthoringDraftValidator.Validate(threshold with
{
    Outputs = [new("o", "O", "Q", "%", new("input", "missing"))]
}), "AUTHORING_REFERENCE_MISSING"), "unknown references are rejected");
Check(HasCode(AuthoringDraftValidator.Validate(threshold with
{
    Inputs = [.. threshold.Inputs, .. threshold.Inputs]
}), "AUTHORING_ID_DUPLICATE"), "duplicate ids are rejected");
Check(HasCode(AuthoringDraftValidator.Validate(Starter(AuthoringStarters.CardCalibrationId) with
{
    Steps =
    [
        Starter(AuthoringStarters.CardCalibrationId).Steps[0] with
        {
            Code = Starter(AuthoringStarters.CardCalibrationId).Steps[0].Code! with
            { Source = AuthoringStarters.CardSource.Replace("\"authored.card-calibration\"", "\"other.id\"", StringComparison.Ordinal) }
        }
    ]
}), "AUTHORING_CODE_IDENTITY_MISMATCH"), "code step identity must match the module identity");

// The card starter reproduces the sample source apart from its identity.
var sample = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "sample", "CardCalibrationModule.cs"));
var normalized = AuthoringStarters.CardSource.Replace("\"authored.card-calibration\"", "\"sample.card-calibration\"", StringComparison.Ordinal);
Check(normalized.ReplaceLineEndings("\n").TrimEnd() == sample.ReplaceLineEndings("\n").TrimEnd(),
    "card starter source must equal the sample apart from module identity");

// Strict document reading and digesting.
var element = AuthoringJson.ToElement(threshold);
Check(AuthoringJson.Digest(AuthoringJson.Read(element)) == AuthoringJson.Digest(threshold), "document round trips");
await Throws<WellSiteAutoPilotException>(() =>
{
    AuthoringJson.Read(JsonDocument.Parse("""{"schema":"wsa-authoring-draft/1","surprise":1}""").RootElement);
    return Task.CompletedTask;
}, "unknown members are rejected");

// Generation is deterministic, escapes literals and keeps manifests consistent.
var first = AuthoringModuleGenerator.Generate(threshold);
Check(first.SourceFiles["EntryPoint.cs"] == AuthoringModuleGenerator.Generate(threshold).SourceFiles["EntryPoint.cs"], "generation is deterministic");
var commandAction = threshold.Actions[0] with { Command = "SetFillageTarget" };
var withCommand = threshold with { Actions = [commandAction] };
Check(AuthoringDraftValidator.Validate(withCommand).IsValid, "a command action with a suggested value is valid");
var commandSource = AuthoringModuleGenerator.Generate(withCommand).SourceFiles["EntryPoint.cs"];
Check(commandSource.Contains("controlIntents.Add(new ControlIntentDraft(", StringComparison.Ordinal) &&
    commandSource.Contains("\"SetFillageTarget\"", StringComparison.Ordinal) &&
    !commandSource.Contains("recommendations.Add(", StringComparison.Ordinal), "a command action generates a control intent, not an advisory draft");
Check(HasCode(AuthoringDraftValidator.Validate(threshold with { Actions = [commandAction with { SuggestedValue = null }] }),
    "AUTHORING_FIELD_REQUIRED"), "a command action needs a suggested value");
Check(HasCode(AuthoringDraftValidator.Validate(threshold with { Actions = [commandAction with { Command = "bad command!" }] }),
    "AUTHORING_FIELD_INVALID"), "a command must be an identifier");
Check(AuthoringModuleGenerator.CSharpString("a\"b\\c\n") == "\"a\\\"b\\\\c\\n\"", "string literals are escaped");
Check(JsonDocument.Parse(first.ManifestJson).RootElement.GetProperty("manifestVersion").GetInt32() == 1, "scalar drafts use manifest v1");
Check(JsonDocument.Parse(AuthoringModuleGenerator.Generate(Starter(AuthoringStarters.CardCalibrationId)).ManifestJson)
    .RootElement.GetProperty("manifestVersion").GetInt32() == 2, "code-step drafts use manifest v2");

// Service: gating, states, builds, artifacts and catalog hand-off.
var root = Path.Combine(AppContext.BaseDirectory, "authoring-test-" + Guid.NewGuid().ToString("N"));
try
{
    var clock = TimeProvider.System;
    AuthoringService Create(bool enabled, out FakePackageRepository repository)
    {
        var options = new AuthoringOptions { Enabled = enabled, StorePath = Path.Combine(root, "store"), WorkRoot = Path.Combine(root, "work") };
        var store = new FileAuthoringDraftStore(options);
        var runner = new AuthoringBuildRunner(store, new LocalDotNetAuthoringBuilder(options, new JobObjectProcessRunner()), clock);
        repository = new FakePackageRepository();
        return new AuthoringService(options, store, runner, new LogicModulePackageService(repository, clock),
            new OutOfProcessAuthoringTestExecutor(options, new JobObjectProcessRunner()), clock);
    }

    var disabled = Create(false, out _);
    var denied = await Throws<WellSiteAutoPilotException>(() => disabled.ListAsync(CancellationToken.None), "disabled gate");
    Check(denied.Code == "AUTHORING_LOCAL_DISABLED" && denied.Kind == FailureKind.NotFound, "disabled flag hides the API");

    var service = Create(true, out var packages);
    var created = await service.CreateAsync(AuthoringStarters.ThresholdAlertId, null, "tester", CancellationToken.None);
    var id = created.Draft.Id;
    Check(created.Draft.Revision == 1 && created.Build == AuthoringBuildState.None && created.Validation == AuthoringValidationState.NotRun
        && created.Test == AuthoringTestState.NotRun, "new drafts start with separate not-run states");
    await Throws<WellSiteAutoPilotException>(() => service.DownloadAsync(id, CancellationToken.None), "no download before build");

    var validated = await service.ValidateAsync(id, CancellationToken.None);
    Check(validated.Validation == AuthoringValidationState.Valid, "starter validates");

    async Task<AuthoringDraftView> WaitForBuild(Guid draftId)
    {
        for (var i = 0; i < 600; i++)
        {
            var view = await service.GetAsync(draftId, CancellationToken.None);
            if (view.Build != AuthoringBuildState.Building) return view;
            await Task.Delay(500);
        }
        throw new TimeoutException("build did not finish");
    }

    var building = await service.BuildAsync(id, CancellationToken.None);
    Check(building.Build == AuthoringBuildState.Building, "build starts in the background");
    var built = await WaitForBuild(id);
    Check(built.Build == AuthoringBuildState.Succeeded,
        "threshold build must succeed: " + string.Join("; ", built.EffectiveBuild?.Diagnostics.Select(d => d.Message) ?? []));
    Check(built.EffectiveBuild!.Revision == 1, "build is pinned to its revision");

    var (package, fileName, sha) = await service.DownloadAsync(id, CancellationToken.None);
    Check(Convert.ToHexStringLower(SHA256.HashData(package)) == sha && fileName.EndsWith(".wsamodule", StringComparison.Ordinal),
        "download matches recorded hash");
    var inspected = await LogicModulePackageReader.ReadAsync(new MemoryStream(package));
    Check(inspected.Manifest.ModuleId == "authored.fillage-threshold-alert", "package carries the authored identity");
    Check(!inspected.ManifestJson.Contains("approved", StringComparison.OrdinalIgnoreCase), "manifest carries no approval claims");

    // Execute the compiled module directly to prove the generated arithmetic.
    using (var archive = new ZipArchive(new MemoryStream(package)))
    {
        var entry = archive.Entries.Single(e => e.FullName == "payload/Authored.Module.dll");
        using var bytes = new MemoryStream();
        await using (var stream = entry.Open()) await stream.CopyToAsync(bytes);
        var module = (ILogicModuleV1)Activator.CreateInstance(Assembly.Load(bytes.ToArray()).GetType("Authored.EntryPoint")!)!;
        var asset = Guid.NewGuid();
        LogicModuleExecutionContext Context(decimal fillage, string settings) => new(Guid.NewGuid(), module.Identity, "corr",
            DateTimeOffset.UtcNow, settings, [new("well", asset, "{}")],
            [new("pump-fillage", asset, "PumpFillage", fillage, "%", DateTimeOffset.UtcNow, "Good", "test")]);
        var low = await module.ExecuteAsync(Context(30m, "{}"));
        Check(low.FailureCode is null && low.EngineeringResults.Single(r => r.Id == "fillage-shortfall").Value == 10m, "shortfall is threshold minus fillage");
        Check(low.EngineeringResults.Single(r => r.Id == "fillage-ratio-result").Value == 0.75m, "ratio is exact decimal division");
        Check(low.Recommendations.Count == 1 && low.Recommendations.Single().SuggestedValue == 40m, "low fillage proposes a review");
        var high = await module.ExecuteAsync(Context(55m, """{"low-threshold":"50"}"""));
        Check(high.Recommendations.Count == 0, "no recommendation above the configured threshold");
        var bad = await module.ExecuteAsync(Context(55m, """{"low-threshold":50}"""));
        Check(bad.FailureCode == "PARAMETER_INVALID", "numeric parameter encodings are rejected");
        var wrong = await module.ExecuteAsync(Context(55m, "{}") with { ModuleIdentity = new("other", "1.0.0") });
        Check(wrong.FailureCode == "MODULE_IDENTITY_MISMATCH", "identity is checked");
    }

    // Test state: fixtures run in the out-of-process host against the built package, with intermediate values.
    var tested = await service.TestAsync(id, CancellationToken.None);
    Check(tested.Test == AuthoringTestState.Passed && tested.EffectiveTest!.Cases.Count == 2,
        "threshold fixtures pass: " + string.Join("; ", tested.EffectiveTest!.Cases.SelectMany(c => c.Mismatches).Concat(tested.EffectiveTest.Diagnostics.Select(d => d.Message))));
    var lowCase = tested.EffectiveTest!.Cases.Single(c => c.FixtureId == "low-fillage");
    Check(lowCase.Intermediates.Any(i => i.Id == "shortfall" && i.Value.GetString() == "10") &&
        lowCase.Intermediates.Any(i => i.Id == "is-low" && i.Value.GetString() == "true"),
        "instrumentation reports intermediate step and condition values");
    Check(lowCase.Actions.SequenceEqual(["REVIEW_LOW_FILLAGE"]), "fired actions are reported");
    Check(tested.EffectiveTest.BuildId == built.EffectiveBuild.BuildId && tested.EffectiveTest.Revision == 1, "the test is pinned to the build and revision");

    // Optimistic concurrency and invalidation.
    await Throws<WellSiteAutoPilotException>(() => service.UpdateAsync(id, 99, AuthoringJson.ToElement(threshold), CancellationToken.None), "stale revision rejected");
    var edited = threshold with { Parameters = [new("low-threshold", "Low fillage threshold", "45", "%")] };
    var updated = await service.UpdateAsync(id, 1, AuthoringJson.ToElement(edited), CancellationToken.None);
    Check(updated.Draft.Revision == 2 && updated.Build == AuthoringBuildState.Stale && updated.Validation == AuthoringValidationState.Stale,
        "an edit stales validation and the build");
    Check(updated.Test == AuthoringTestState.Stale, "an edit stales the test result");
    var conflict = await Throws<WellSiteAutoPilotException>(() => service.DownloadAsync(id, CancellationToken.None), "stale build download");
    Check(conflict.Code == "AUTHORING_BUILD_NOT_CURRENT", "stale builds cannot be downloaded");
    await Throws<WellSiteAutoPilotException>(() => service.AddToCatalogAsync(id, new AccessSubject(Guid.NewGuid()),
        new SecurityActorContext(null, "tester", null), CancellationToken.None), "stale build cannot be cataloged");
    var unchanged = await service.UpdateAsync(id, 2, AuthoringJson.ToElement(edited), CancellationToken.None);
    Check(unchanged.Draft.Revision == 2, "identical content does not bump the revision");

    var invalid = edited with { Outputs = [new("o", "O", "Q", "%", new("input", "nope"))] };
    await service.UpdateAsync(id, 2, AuthoringJson.ToElement(invalid), CancellationToken.None);
    var blocked = await Throws<WellSiteAutoPilotException>(() => service.BuildAsync(id, CancellationToken.None), "invalid build");
    Check(blocked.Code == "AUTHORING_DRAFT_INVALID", "invalid drafts do not build");
    await service.UpdateAsync(id, 3, AuthoringJson.ToElement(edited), CancellationToken.None);

    await service.BuildAsync(id, CancellationToken.None);
    var rebuilt = await WaitForBuild(id);
    Check(rebuilt.Build == AuthoringBuildState.Succeeded && rebuilt.Draft.Revision == 4, "rebuild succeeds for the new revision");
    var regressed = await service.TestAsync(id, CancellationToken.None);
    Check(regressed.Test == AuthoringTestState.Failed &&
        regressed.EffectiveTest!.Cases.Any(c => c.Mismatches.Any(m => m.Contains("fillage-shortfall", StringComparison.Ordinal))),
        "a changed parameter fails the fixture expectations");

    var installed = await service.AddToCatalogAsync(id, new AccessSubject(Guid.NewGuid()), new SecurityActorContext(null, "tester", null),
        CancellationToken.None);
    Check(installed.TrustStatus == LogicModuleTrustStatus.Untrusted && !installed.IsEnabled, "catalog entry is untrusted and disabled");
    Check(installed.PackageSha256 == rebuilt.EffectiveBuild!.PackageSha256 && packages.Uploads == 1, "catalog receives the exact artifact");
    Check((await service.GetAsync(id, CancellationToken.None)).EffectiveBuild!.Catalog is not null, "submission is recorded");

    // Compile failures surface as diagnostics, not exceptions.
    var card = await service.CreateAsync(AuthoringStarters.CardCalibrationId, null, "tester", CancellationToken.None);
    await service.BuildAsync(card.Draft.Id, CancellationToken.None);
    var cardBuilt = await WaitForBuild(card.Draft.Id);
    Check(cardBuilt.Build == AuthoringBuildState.Succeeded,
        "card calibration build must succeed: " + string.Join("; ", cardBuilt.EffectiveBuild?.Diagnostics.Select(d => d.Message) ?? []));
    var cardPackage = await service.DownloadAsync(card.Draft.Id, CancellationToken.None);
    var cardInspected = await LogicModulePackageReader.ReadAsync(new MemoryStream(cardPackage.Package));
    Check(cardInspected.Manifest.ModuleId == "authored.card-calibration", "card package carries its identity");
    var cardTested = await service.TestAsync(card.Draft.Id, CancellationToken.None);
    Check(cardTested.Test == AuthoringTestState.Passed,
        "card calibration runs through the V2 host: " + string.Join("; ", cardTested.EffectiveTest!.Cases.SelectMany(c => c.Mismatches)
            .Concat(cardTested.EffectiveTest.Diagnostics.Select(d => d.Message)).Concat(cardTested.EffectiveTest.Cases.Select(c => c.Message ?? ""))));

    var broken = Starter(AuthoringStarters.CardCalibrationId);
    broken = broken with
    {
        Steps = [broken.Steps[0] with { Code = broken.Steps[0].Code! with { Source = broken.Steps[0].Code!.Source + "\nclass Broken { int x = \"text\"; }" } }]
    };
    var brokenDraft = await service.CreateAsync(null, AuthoringJson.ToElement(broken), "tester", CancellationToken.None);
    await service.BuildAsync(brokenDraft.Draft.Id, CancellationToken.None);
    var failed = await WaitForBuild(brokenDraft.Draft.Id);
    Check(failed.Build == AuthoringBuildState.Failed && failed.EffectiveBuild!.Diagnostics.Any(d => d.Message.Contains("CS0029", StringComparison.Ordinal)),
        "compiler errors are reported as diagnostics");
    Check(!failed.EffectiveBuild!.Diagnostics.Any(d => d.Message.Contains(root, StringComparison.OrdinalIgnoreCase) || d.Path.Contains(root, StringComparison.OrdinalIgnoreCase)),
        "diagnostics do not leak work paths");
    await Throws<WellSiteAutoPilotException>(() => service.DownloadAsync(brokenDraft.Draft.Id, CancellationToken.None), "failed build download");
}
finally
{
    try { Directory.Delete(root, recursive: true); }
    catch (IOException) { }
}

// Process isolation: Job object cancellation, timeout, memory limit and the optional AppContainer.
if (OperatingSystem.IsWindows())
{
    var isolationRoot = Path.Combine(Path.GetTempPath(), "wsa-isolation-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(isolationRoot);
    var runner = new JobObjectProcessRunner();
    var baseEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot")!, ["PATH"] = Environment.GetEnvironmentVariable("PATH")!,
        ["TEMP"] = Path.GetTempPath(), ["TMP"] = Path.GetTempPath()
    };
    IsolatedProcessSpec Spec(string script, TimeSpan timeout, long memory = 2L * 1024 * 1024 * 1024, bool container = false) =>
        new("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", script], isolationRoot, baseEnvironment, memory, timeout, container, [isolationRoot]);
    static async Task<int> WaitForPidAsync(string pidFile)
    {
        for (var i = 0; i < 200; i++)
        {
            if (File.Exists(pidFile) && int.TryParse((await File.ReadAllTextAsync(pidFile)).Trim(), out var pid)) return pid;
            await Task.Delay(100);
        }
        throw new InvalidOperationException("Authoring check failed: the grandchild never started.");
    }
    static bool Alive(int pid)
    {
        try { return !System.Diagnostics.Process.GetProcessById(pid).HasExited; }
        catch (ArgumentException) { return false; }
    }
    static async Task WaitDeadAsync(int pid, string message)
    {
        for (var i = 0; i < 100 && Alive(pid); i++) await Task.Delay(100);
        Check(!Alive(pid), message);
    }
    string Grandchild(string pidFile) =>
        $"$p = Start-Process ping -ArgumentList '-n','300','127.0.0.1' -PassThru -WindowStyle Hidden; $p.Id | Out-File -Encoding ascii '{pidFile}'; Start-Sleep 300";

    var cancelPid = Path.Combine(isolationRoot, "cancel.pid");
    using (var cancel = new CancellationTokenSource())
    {
        var running = runner.RunAsync(Spec(Grandchild(cancelPid), TimeSpan.FromMinutes(5)), cancel.Token);
        var child = await WaitForPidAsync(cancelPid);
        Check(Alive(child), "the grandchild is running before cancellation");
        cancel.Cancel();
        await Throws<OperationCanceledException>(() => running, "cancellation surfaces to the caller");
        await WaitDeadAsync(child, "cancellation kills the whole process tree, including the grandchild");
    }

    var timeoutPid = Path.Combine(isolationRoot, "timeout.pid");
    var timedOut = await runner.RunAsync(Spec(Grandchild(timeoutPid), TimeSpan.FromSeconds(4)), CancellationToken.None);
    Check(timedOut.TimedOut && timedOut.Isolated && timedOut.Mode == "job", "a runaway process is reported as timed out under the job");
    await WaitDeadAsync(await WaitForPidAsync(timeoutPid), "a timeout kills the grandchild too");

    var quick = await runner.RunAsync(Spec("Write-Output 'hello'", TimeSpan.FromSeconds(60)), CancellationToken.None);
    Check(quick.ExitCode == 0 && quick.Output.Contains("hello", StringComparison.Ordinal) && !quick.TimedOut, "a normal process returns its output");

    var hungry = await runner.RunAsync(Spec("$ErrorActionPreference = 'Stop'; $a = [byte[]]::new(900MB); for ($i = 0; $i -lt $a.Length; $i += 4096) { $a[$i] = 1 }; Write-Output 'allocated'",
        TimeSpan.FromSeconds(60), memory: 300L * 1024 * 1024), CancellationToken.None);
    Check(hungry.ExitCode != 0 && hungry.Output.Contains("OutOfMemory", StringComparison.Ordinal) && !hungry.Output.Contains("allocated", StringComparison.Ordinal), "the job memory limit stops a process that exceeds it");

    // Optional hardened mode: only meaningful where an AppContainer profile can be created for this account.
    var secret = Path.Combine(Path.GetTempPath(), "wsa-outside-" + Guid.NewGuid().ToString("N") + ".txt");
    await File.WriteAllTextAsync(secret, "outside");
    try
    {
        var granted = Path.Combine(isolationRoot, "granted.txt");
        await File.WriteAllTextAsync(granted, "inside");
        try
        {
            var inside = await runner.RunAsync(Spec($"Get-Content '{granted}'", TimeSpan.FromSeconds(60), container: true), CancellationToken.None);
            var outside = await runner.RunAsync(Spec($"Get-Content '{secret}'", TimeSpan.FromSeconds(60), container: true), CancellationToken.None);
            Check(inside.Mode == "job+appcontainer" && inside.Output.Contains("inside", StringComparison.Ordinal), "an AppContainer child can read its granted folder");
            Check(!outside.Output.Contains("outside", StringComparison.Ordinal), "an AppContainer child cannot read a file outside its grant");
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            Console.WriteLine("AppContainer isolation unavailable in this environment, skipped: " + error.Message);
        }
    }
    finally { File.Delete(secret); }
    try { Directory.Delete(isolationRoot, recursive: true); }
    catch (IOException) { }
}
{
    var read = new WellSiteAutoPilot.Application.Executions.AuthorizedExecutionRead("cygnet", Guid.NewGuid(), 1, "FAC1", "PumpFillage",
        new Dictionary<string, string?>());
    var source = new FakePreviewSource(read);
    var gateway = new FakeGateway();
    var subject = new AccessSubject(Guid.NewGuid());
    var asset = Guid.NewGuid();
    var preview = new AuthoringPreviewService(new AuthoringOptions { Enabled = true }, source, gateway);
    var value = await preview.PreviewAsync(subject, asset, " PumpFillage ", CancellationToken.None);
    Check(value.Value == 42.5m && value.Unit == "%" && value.AdapterKey == "cygnet", "preview returns the gateway value with its adapter");
    Check(source.LastQuantity == "PumpFillage" && source.LastSubject == subject && gateway.Reads == 1, "preview resolves with the caller's subject and trimmed quantity");
    await Throws<WellSiteAutoPilotException>(() => new AuthoringPreviewService(new AuthoringOptions { Enabled = false }, source, gateway)
        .PreviewAsync(subject, asset, "PumpFillage", CancellationToken.None), "preview honours the local gate");
    await Throws<WellSiteAutoPilotException>(() => preview.PreviewAsync(subject, Guid.Empty, "PumpFillage", CancellationToken.None), "preview needs an asset");
    await Throws<WellSiteAutoPilotException>(() => preview.PreviewAsync(subject, asset, " ", CancellationToken.None), "preview needs a quantity");
    source.Deny = true;
    var reads = gateway.Reads;
    await Throws<WellSiteAutoPilotException>(() => preview.PreviewAsync(subject, asset, "PumpFillage", CancellationToken.None), "denied source blocks the read");
    Check(gateway.Reads == reads, "a denied preview never reaches the provider gateway");
}
Console.WriteLine("Authoring checks passed.");

internal sealed class FakePreviewSource(WellSiteAutoPilot.Application.Executions.AuthorizedExecutionRead read) : IAuthoringPreviewSource
{
    public bool Deny { get; set; }
    public string? LastQuantity { get; private set; }
    public AccessSubject? LastSubject { get; private set; }

    public Task<WellSiteAutoPilot.Application.Executions.AuthorizedExecutionRead> ResolveAsync(AccessSubject subject, Guid assetId,
        string quantity, CancellationToken cancellationToken = default)
    {
        LastSubject = subject; LastQuantity = quantity;
        return Deny ? throw new WellSiteAutoPilotException("DENIED", FailureKind.Authorization, "denied") : Task.FromResult(read);
    }
}

internal sealed class FakeGateway : WellSiteAutoPilot.Application.Executions.IExecutionMeasurementGateway
{
    public int Reads { get; private set; }

    public Task<WellSiteAutoPilot.Integration.Contracts.Providers.EngineeringValue> GetCurrentAsync(
        WellSiteAutoPilot.Application.Executions.AuthorizedExecutionRead read, CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult(new WellSiteAutoPilot.Integration.Contracts.Providers.EngineeringValue(read.Quantity, 42.5m, "%",
            DateTimeOffset.UnixEpoch, "Good", "fake"));
    }
}

internal sealed class FakePackageRepository : ILogicModulePackageRepository
{
    public int Uploads { get; private set; }

    public Task<InstalledLogicModule> UploadAuthorizedAsync(InstalledLogicModule logicModule, byte[] archive,
        AccessSubject subject, SecurityActorContext actor, CancellationToken cancellationToken = default)
    {
        Uploads++;
        return Task.FromResult(logicModule);
    }
}
