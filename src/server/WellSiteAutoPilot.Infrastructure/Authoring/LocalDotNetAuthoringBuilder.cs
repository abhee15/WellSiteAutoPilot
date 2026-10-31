using System.Text;
using System.Text.RegularExpressions;
using WellSiteAutoPilot.Application.Authoring;
using WellSiteAutoPilot.Application.Logic;

namespace WellSiteAutoPilot.Infrastructure.Authoring;

/// <summary>
/// Trusted local mode: compiles generated source with the local .NET SDK in a scratch folder and a scrubbed
/// environment, inside a Job object (memory limit, kill-on-close, tree cancellation) and optionally an AppContainer.\n/// Even so this is a local proof-of-concept boundary; hosted builds still need a separate sandboxed worker.
/// </summary>
public sealed partial class LocalDotNetAuthoringBuilder(AuthoringOptions options, IIsolatedProcessRunner runner) : IAuthoringBuilder
{
    private const string SdkProjectFile = "WellSiteAutoPilot.ModuleSdk.csproj";
    private const int MaximumOutputCharacters = 64 * 1024;

    private static readonly string[] InheritedEnvironment =
    [
        "PATH", "SystemRoot", "SystemDrive", "ComSpec", "PATHEXT", "TEMP", "TMP", "USERPROFILE", "HOMEDRIVE", "HOMEPATH",
        "APPDATA", "LOCALAPPDATA", "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "DOTNET_ROOT", "NUGET_PACKAGES"
    ];

    [GeneratedRegex(@"error (CS\d{4}|MSB\d{4}|NETSDK\d{4}|NU\d{4}): (?<message>[^\r\n\[]*)", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorPattern();

    public async Task<AuthoringBuilderResult> BuildAsync(AuthoringGeneratedProject project, CancellationToken cancellationToken)
    {
        var sdk = ResolveSdkDirectory();
        if (sdk is null)
            return Failure("AUTHORING_SDK_NOT_FOUND",
                "The product SDK project was not found. Set Authoring:LocalBuild:SdkProjectPath.");
        var workRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(options.WorkRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WellSiteAutoPilot", "authoring-work")
            : options.WorkRoot);
        var work = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            await File.WriteAllTextAsync(Path.Combine(work, "Directory.Build.props"), BuildProps, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(work, "Directory.Build.targets"), "<Project />", cancellationToken);
            var sdkCopy = Path.Combine(work, "sdk");
            Directory.CreateDirectory(sdkCopy);
            foreach (var file in Directory.EnumerateFiles(sdk, "*.cs").Append(Path.Combine(sdk, SdkProjectFile)))
                File.Copy(file, Path.Combine(sdkCopy, Path.GetFileName(file)));
            var module = Path.Combine(work, "module");
            Directory.CreateDirectory(module);
            foreach (var (name, source) in project.SourceFiles)
            {
                if (name.Contains('/') || name.Contains('\\') || name.Contains("..", StringComparison.Ordinal))
                    return Failure("AUTHORING_GENERATED_NAME_INVALID", "A generated file name was not acceptable.");
                await File.WriteAllTextAsync(Path.Combine(module, name), source, new UTF8Encoding(false), cancellationToken);
            }
            await File.WriteAllTextAsync(Path.Combine(module, project.AssemblyName + ".csproj"), ModuleProject, cancellationToken);

            var output = Path.Combine(work, "out");
            var (exitCode, text) = await RunAsync(work, module, output, cancellationToken);
            if (exitCode != 0)
            {
                var diagnostics = ParseDiagnostics(text, work);
                return new(false, null, diagnostics.Count > 0 ? diagnostics :
                    [Issue("AUTHORING_COMPILE_FAILED", "build", "The compiler reported a failure without a recognized diagnostic.")]);
            }
            var payload = Path.Combine(work, "payload");
            Directory.CreateDirectory(payload);
            foreach (var dll in Directory.EnumerateFiles(output, "*.dll"))
                if (!Path.GetFileName(dll).Equals("WellSiteAutoPilot.ModuleSdk.dll", StringComparison.OrdinalIgnoreCase))
                    File.Copy(dll, Path.Combine(payload, Path.GetFileName(dll)));
            if (!Directory.EnumerateFiles(payload).Any())
                return Failure("AUTHORING_COMPILE_FAILED", "The build produced no module assembly.");
            var manifestPath = Path.Combine(work, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, project.ManifestJson, new UTF8Encoding(false), cancellationToken);
            using var package = new MemoryStream();
            await LogicModulePackageWriter.WriteAsync(package, manifestPath, payload, cancellationToken);
            package.Position = 0;
            await LogicModulePackageReader.ReadAsync(package, cancellationToken);
            return new(true, package.ToArray(), []);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private string? ResolveSdkDirectory()
    {
        if (!string.IsNullOrWhiteSpace(options.SdkProjectPath))
        {
            var configured = Path.GetFullPath(options.SdkProjectPath);
            if (Directory.Exists(configured)) configured = Path.Combine(configured, SdkProjectFile);
            return File.Exists(configured) ? Path.GetDirectoryName(configured) : null;
        }
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "src", "modules", "WellSiteAutoPilot.ModuleSdk");
                if (File.Exists(Path.Combine(candidate, SdkProjectFile))) return candidate;
            }
        return null;
    }

    private async Task<(int ExitCode, string Output)> RunAsync(string work, string projectDirectory, string output,
        CancellationToken cancellationToken)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in InheritedEnvironment)
            if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        environment["DOTNET_NOLOGO"] = "1";
        environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        environment["MSBUILDDISABLENODEREUSE"] = "1";
        environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        var appContainer = options.Isolation == AuthoringIsolationMode.AppContainer && OperatingSystem.IsWindows();
        if (appContainer)
        {
            // The container cannot see the user profile, so everything the build writes lives in the work folder.
            var home = Path.Combine(work, "home");
            Directory.CreateDirectory(home);
            foreach (var name in new[] { "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "DOTNET_CLI_HOME" })
                environment[name] = home;
            environment["NUGET_PACKAGES"] = Path.Combine(home, "packages");
        }
        var arguments = new[] { "publish", "-c", "Release", "-o", output, "--nologo", "-v:q", "-p:UseAppHost=false", "-p:UseSharedCompilation=false" };
        var result = await runner.RunAsync(new IsolatedProcessSpec(
            string.IsNullOrWhiteSpace(options.DotnetPath) ? "dotnet" : options.DotnetPath, arguments, projectDirectory,
            environment, Math.Clamp(options.BuildMemoryLimitMegabytes, 256, 65536) * 1024L * 1024L,
            TimeSpan.FromSeconds(Math.Clamp(options.BuildTimeoutSeconds, 10, 1800)), appContainer, [work]), cancellationToken);
        return result.TimedOut ? (-1, "error MSB0000: The build exceeded its time limit.") : (result.ExitCode, result.Output);
    }
    private static List<AuthoringIssue> ParseDiagnostics(string text, string work)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var issues = new List<AuthoringIssue>();
        foreach (var line in text.Split('\n'))
        {
            var cleaned = line.Replace(work, "<work>", StringComparison.OrdinalIgnoreCase).Trim();
            var match = ErrorPattern().Match(cleaned);
            if (!match.Success || !seen.Add(cleaned) || issues.Count >= 20) continue;
            var location = cleaned[..Math.Max(0, match.Index)].Trim().TrimEnd(':').Trim();
            var message = $"{match.Groups[1].Value}: {match.Groups["message"].Value.Trim()}";
            issues.Add(Issue("AUTHORING_COMPILE_ERROR", string.IsNullOrEmpty(location) ? "build" : location, message));
        }
        return issues;
    }

    private static AuthoringIssue Issue(string code, string path, string message) =>
        new(AuthoringIssueSeverity.Error, code, path, message);

    private static AuthoringBuilderResult Failure(string code, string message) => new(false, null, [Issue(code, "build", message)]);

    private const string BuildProps = """
        <Project>
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <LangVersion>latest</LangVersion>
            <Deterministic>true</Deterministic>
            <DebugType>none</DebugType>
            <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
            <EnableNETAnalyzers>false</EnableNETAnalyzers>
            <GenerateDocumentationFile>false</GenerateDocumentationFile>
          </PropertyGroup>
        </Project>
        """;

    private const string ModuleProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <AssemblyName>Authored.Module</AssemblyName>
            <RootNamespace>Authored</RootNamespace>
            <GenerateDependencyFile>false</GenerateDependencyFile>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="..\sdk\WellSiteAutoPilot.ModuleSdk.csproj" Private="false" />
          </ItemGroup>
        </Project>
        """;
}
