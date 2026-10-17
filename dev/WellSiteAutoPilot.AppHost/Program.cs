// Developer tooling only (see docs/development/aspire-local-development-plan.md).
// Increment 1a: NATS (your own nats-server), the Server and the React frontend.
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Per-developer settings: gitignored file plus user-secrets for the password.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

var auth = builder.Configuration["Auth"] ?? "TestHeader";
if (auth is not ("TestHeader" or "Negotiate"))
{
    throw new InvalidOperationException("Auth must be TestHeader or Negotiate.");
}

var connectionString = builder.Configuration["Database:ConnectionString"]
    ?? throw new InvalidOperationException(
        "Set Database:ConnectionString in appsettings.Local.json (see appsettings.Local.example.json). " +
        "Keep the password in user-secrets, not in the file.");
var bootstrapAdministrator = builder.Configuration["Identity:BootstrapAdministrator"]
    ?? throw new InvalidOperationException("Set Identity:BootstrapAdministrator in appsettings.Local.json.");
const string natsUrl = "nats://127.0.0.1:4222";

IResourceBuilder<ExecutableResource>? nats = null;
if (!string.Equals(builder.Configuration["Nats:Mode"], "External", StringComparison.OrdinalIgnoreCase))
{
    var natsPath = builder.Configuration["Nats:ServerPath"]
        ?? throw new InvalidOperationException("Set Nats:ServerPath, or set Nats:Mode to External.");
    var store = builder.Configuration["Nats:StoreDirectory"]
        ?? throw new InvalidOperationException("Set Nats:StoreDirectory.");
    Directory.CreateDirectory(store);
    nats = builder.AddExecutable(
        "nats", natsPath, Path.GetDirectoryName(Path.GetFullPath(natsPath))!,
        "-js", "-a", "127.0.0.1", "-p", "4222", "-sd", store);
}

var server = builder.AddProject<Projects.WellSiteAutoPilot_Server>("server")
    .WithHttpEndpoint(port: 5080, targetPort: 5080, name: "http", isProxied: false)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Security__AuthenticationMode", auth)
    .WithEnvironment("Security__BootstrapAdministrators__0", bootstrapAdministrator)
    .WithEnvironment("Messaging__Nats__Url", natsUrl)
    .WithEnvironment("ConnectionStrings__WellSiteAutoPilot", connectionString);
if (nats is not null)
{
    server.WaitFor(nats);
}

// TestHeader: the Vite dev server is the browser origin and injects the test identity.
// Windows: Negotiate cannot go through Vite; the browser uses the Server directly
// (frontend serving for that profile arrives in increment 3).
if (auth == "TestHeader")
{
    builder.AddViteApp("web", "../../src/web")
        .WithEnvironment("WSA_DEV_AUTH_MODE", "TestHeader")
        .WithEnvironment("WSA_DEV_TEST_IDENTITY", bootstrapAdministrator)
        .WithEnvironment("WSA_DEV_API_TARGET", "http://127.0.0.1:5080")
        .WaitFor(server);
}

builder.Build().Run();
