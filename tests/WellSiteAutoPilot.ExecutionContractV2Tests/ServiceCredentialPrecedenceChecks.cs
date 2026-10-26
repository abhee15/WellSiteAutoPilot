using Microsoft.Extensions.Configuration;
using WellSiteAutoPilot.Http.ServiceCallers;

internal static class ServiceCredentialPrecedenceChecks
{
    public static void Run()
    {
        string[] names = ["Security__ServiceKey", "Messaging__Nats__Token", "Security__AuthenticationMode"];
        var previous = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(names[0], "explicit-caller-test-credential");
            Environment.SetEnvironmentVariable(names[1], "explicit-broker-test-credential");
            Environment.SetEnvironmentVariable(names[2], "MustNotOverrideOtherSettings");
            using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:ServiceKey"] = "stored-caller", ["Messaging:Nats:Token"] = "stored-broker",
                ["Security:AuthenticationMode"] = "Negotiate"
            }).AddServiceCallerSettingsFile("absent-" + Guid.NewGuid().ToString("N") + ".json").Build();
            if (configuration["Security:ServiceKey"] != "explicit-caller-test-credential" ||
                configuration["Messaging:Nats:Token"] != "explicit-broker-test-credential" ||
                configuration["Security:AuthenticationMode"] != "Negotiate")
                throw new InvalidOperationException("Service credential precedence changed unrelated authentication settings or ignored explicit overrides.");
        }
        finally { foreach (var name in names) Environment.SetEnvironmentVariable(name, previous[name]); }
    }
}
