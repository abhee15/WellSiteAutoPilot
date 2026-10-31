using System.Text.Json;
using WellSiteAutoPilot.Application.Authoring;
using WellSiteAutoPilot.Failures;

namespace WellSiteAutoPilot.Infrastructure.Authoring;

/// <summary>
/// Runs the authoring test host as a separate process under a job object (memory limit, timeout, kill-on-close),
/// so authored code never executes inside the Server process.
/// </summary>
public sealed class OutOfProcessAuthoringTestExecutor(AuthoringOptions options, IIsolatedProcessRunner runner)
    : IAuthoringTestExecutor
{
    private const string HostAssembly = "WellSiteAutoPilot.AuthoringTestHost.dll";
    private static readonly string[] InheritedEnvironment =
        ["PATH", "SystemRoot", "windir", "DOTNET_ROOT", "ProgramFiles", "ProgramData", "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE"];

    public bool Available => ResolveHost() is not null;

    public async Task<AuthoringTestHostResult> RunAsync(byte[] package, AuthoringTestHostRequest request,
        CancellationToken cancellationToken)
    {
        var host = ResolveHost() ?? throw new WellSiteAutoPilotException("AUTHORING_TEST_HOST_UNAVAILABLE",
            FailureKind.DependencyUnavailable, "The authoring test host was not found.");
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(options.WorkRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WellSiteAutoPilot", "authoring-work")
            : options.WorkRoot);
        var work = Path.Combine(root, "test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var packagePath = Path.Combine(work, "package.wsamodule");
            var requestPath = Path.Combine(work, "request.json");
            var resultPath = Path.Combine(work, "result.json");
            await File.WriteAllBytesAsync(packagePath, package, cancellationToken);
            await File.WriteAllTextAsync(requestPath,
                JsonSerializer.Serialize(request, JsonSerializerOptions.Web), cancellationToken);
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in InheritedEnvironment)
                if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
            environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            environment["DOTNET_NOLOGO"] = "1";
            var process = await runner.RunAsync(new IsolatedProcessSpec(
                string.IsNullOrWhiteSpace(options.DotnetPath) ? "dotnet" : options.DotnetPath,
                [host, packagePath, requestPath, resultPath, Path.Combine(work, "modules")], work, environment,
                1024L * 1024L * 1024L, TimeSpan.FromSeconds(Math.Clamp(options.TestTimeoutSeconds, 5, 600))),
                cancellationToken);
            if (process.TimedOut)
                return new([], "The test host exceeded its time limit and was stopped.");
            if (!File.Exists(resultPath))
                return new([], "The test host produced no result (exit code " + process.ExitCode + ").");
            return JsonSerializer.Deserialize<AuthoringTestHostResult>(
                await File.ReadAllTextAsync(resultPath, cancellationToken),
                JsonSerializerOptions.Web) ?? new([], "The test host result was empty.");
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private string? ResolveHost()
    {
        if (!string.IsNullOrWhiteSpace(options.TestHostPath))
        {
            var configured = Path.GetFullPath(options.TestHostPath);
            if (Directory.Exists(configured)) configured = Path.Combine(configured, HostAssembly);
            return File.Exists(configured) ? configured : null;
        }
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var tool = Path.Combine(directory.FullName, "tools", "WellSiteAutoPilot.AuthoringTestHost", "bin");
                if (!Directory.Exists(tool)) continue;
                var newest = Directory.EnumerateFiles(tool, HostAssembly, SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (newest is not null) return newest;
            }
        return null;
    }
}
