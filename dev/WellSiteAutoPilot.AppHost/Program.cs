// Developer tooling only (see docs/development/aspire-local-development-plan.md).
// Starts the local system against the developer's installed PostgreSQL and own nats-server.
using System.Net.NetworkInformation;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using WellSiteAutoPilot.AppHost;
using WellSiteAutoPilot.Dev.Core;

var builder = DistributedApplication.CreateBuilder(args);

// Per-developer settings: gitignored file plus user-secrets for the password.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
var config = builder.Configuration;

var repoRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));
var webRoot = Path.Combine(repoRoot, "src", "web");

var auth = config["Auth"] ?? "TestHeader";
if (auth is not ("TestHeader" or "Negotiate"))
{
    throw new InvalidOperationException("Auth must be TestHeader or Negotiate.");
}

var connectionString = config["Database:ConnectionString"]
    ?? throw new InvalidOperationException(
        "Set Database:ConnectionString in appsettings.Local.json (see appsettings.Local.example.json). " +
        "Keep the password in user-secrets as Database:Password, not in the file.");
var databaseMode = DatabaseSettings.ParseMode(config["Database:Mode"]);
var database = DatabaseSettings.Parse(connectionString, config["Database:TemplateName"], config["Database:Password"]);

// TestHeader uses a fake local identity; Windows defaults to the account running this AppHost.
var bootstrapAdministrator = config["Identity:BootstrapAdministrator"]
    ?? (auth == "Negotiate"
        ? $"{Environment.UserDomainName}\\{Environment.UserName}"
        : "LOCAL\\wsa-dev-operator");

const string natsUrl = "nats://127.0.0.1:4222";
var natsExternal = string.Equals(config["Nats:Mode"], "External", StringComparison.OrdinalIgnoreCase);

// Ports the installed product also uses. Fail early and plainly if something is in the way.
var required = new List<PortRequirement>
{
    new("Server", 5080),
    new("Integration Gateway", 5081),
    new(".NET worker", 5082),
    new("Provider Simulator", 5091)
};
if (!natsExternal)
{
    required.Add(new PortRequirement("NATS", 4222));
}

if (auth == "TestHeader")
{
    required.Add(new PortRequirement("Vite frontend", 5173));
}

builder.Eventing.Subscribe<BeforeStartEvent>(async (_, cancellationToken) =>
{
    var listening = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(e => e.Port).ToHashSet();
    var conflicts = PortCheck.FindConflicts(required, listening.Contains);
    if (conflicts.Count > 0)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, conflicts));
    }

    await DatabasePreparation.RunAsync(databaseMode, database, cancellationToken);
});

IResourceBuilder<ExecutableResource>? nats = null;
if (!natsExternal)
{
    var natsPath = config["Nats:ServerPath"]
        ?? throw new InvalidOperationException("Set Nats:ServerPath, or set Nats:Mode to External.");
    var store = config["Nats:StoreDirectory"]
        ?? throw new InvalidOperationException("Set Nats:StoreDirectory.");
    Directory.CreateDirectory(store);
    nats = builder.AddExecutable(
        "nats", natsPath, Path.GetDirectoryName(Path.GetFullPath(natsPath))!,
        "-js", "-a", "127.0.0.1", "-p", "4222", "-sd", store);
}

IResourceBuilder<T> OnNats<T>(IResourceBuilder<T> resource) where T : IResourceWithWaitSupport
{
    if (nats is not null)
    {
        resource.WaitFor(nats);
    }

    return resource;
}

var migrator = builder.AddProject<Projects.WellSiteAutoPilot_DatabaseMigrator>("migrator")
    .WithEnvironment("WSA_DATABASE_CONNECTION_STRING", database.DatabaseConnectionString);

// Ports and environment are pinned below, so no project's launchSettings.json profile is used.
// Without launchProfileName: null, Aspire also creates http/https endpoints from that file and the
// explicit "http" endpoints below collide with them.
var simulator = builder.AddProject<Projects.WellSiteAutoPilot_ProviderSimulator>("provider-simulator", launchProfileName: null)
    .WithHttpEndpoint(port: 5091, targetPort: 5091, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

// Internal callers authenticate to the Gateway with random keys made fresh for each local run; the Gateway
// receives only their SHA-256 verifiers, exactly as the installer arranges for an installed product.
static string NewServiceKey() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
static string VerifierOf(string key) =>
    Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
var serverServiceKey = NewServiceKey();
var workerServiceKey = NewServiceKey();

var gateway = OnNats(builder.AddProject<Projects.WellSiteAutoPilot_IntegrationGateway>("integration-gateway", launchProfileName: null)
    .WithHttpEndpoint(port: 5081, targetPort: 5081, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Security__Callers__server__Verifier", VerifierOf(serverServiceKey))
    .WithEnvironment("Security__Callers__worker__Verifier", VerifierOf(workerServiceKey))
    .WithEnvironment("Messaging__Nats__Url", natsUrl))
    .WaitFor(simulator);

var worker = OnNats(builder.AddProject<Projects.WellSiteAutoPilot_Worker_DotNet>("dotnet-worker", launchProfileName: null)
    .WithHttpEndpoint(port: 5082, targetPort: 5082, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Security__ServiceKey", workerServiceKey)
    .WithEnvironment("Messaging__Nats__Url", natsUrl))
    .WaitFor(gateway);

if (!string.Equals(config["Python:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
{
    var pythonSource = Path.Combine(repoRoot, "src", "workers", "python", "src");
    OnNats(builder.AddExecutable(
            "python-worker", config["Python:Executable"] ?? "python", repoRoot,
            "-m", "wellsite_autopilot_worker.main")
        .WithEnvironment("PYTHONPATH", pythonSource));
}

var server = OnNats(builder.AddProject<Projects.WellSiteAutoPilot_Server>("server", launchProfileName: null)
    .WithHttpEndpoint(port: 5080, targetPort: 5080, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Swagger__Enabled", "true")
    .WithEnvironment("Security__AuthenticationMode", auth)
    .WithEnvironment("Security__BootstrapAdministrators__0", bootstrapAdministrator)
    .WithEnvironment("Messaging__Nats__Url", natsUrl)
    .WithEnvironment("ConnectionStrings__WellSiteAutoPilot", database.DatabaseConnectionString))
    .WaitForCompletion(migrator)
    .WaitFor(gateway)
    .WaitFor(worker);

// The generated API client is not committed. Build it from the running Server's OpenAPI document.
var apiClient = builder.AddExecutable(
        "api-client", "node", repoRoot,
        Path.Combine(repoRoot, "dev", "scripts", "generate-api-client.mjs"))
    .WithEnvironment("WSA_API_BASE", "http://127.0.0.1:5080")
    .WithEnvironment("WSA_REPO_ROOT", repoRoot)
    .WaitFor(server);

if (auth == "TestHeader")
{
    // The Vite dev server is the browser origin and injects the test identity for /api only.
    builder.AddViteApp("web", webRoot)
        .WithEnvironment("WSA_DEV_AUTH_MODE", "TestHeader")
        .WithEnvironment("WSA_DEV_TEST_IDENTITY", bootstrapAdministrator)
        .WithEnvironment("WSA_DEV_API_TARGET", "http://127.0.0.1:5080")
        .WaitForCompletion(apiClient);
}
else
{
    // Negotiate cannot go through Vite. The Server serves the built output straight from
    // src/web/dist (Development only), and a watch build keeps it current: no copy step.
    server.WithEnvironment("Frontend__DevelopmentStaticRoot", Path.Combine(webRoot, "dist"));
    builder.AddExecutable("web-build-watch", "npx", webRoot, "vite", "build", "--watch")
        .WaitForCompletion(apiClient);
}

builder.Build().Run();
