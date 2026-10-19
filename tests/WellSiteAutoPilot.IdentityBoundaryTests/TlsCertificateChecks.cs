using Microsoft.Extensions.Configuration;
using WellSiteAutoPilot.Server;

/// <summary>
/// The installed Server finds its HTTPS certificate by thumbprint; without one it stays plain HTTP.
/// </summary>
internal static class TlsCertificateChecks
{
    private const string Thumbprint = "0123456789ABCDEF0123456789ABCDEF01234567";

    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wsa-tls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "server.settings.json");
            var none = new ConfigurationBuilder().Build();
            var configured = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Tls:Thumbprint"] = "01 23 45 67 89 ab cd ef 01 23 45 67 89 ab cd ef 01 23 45 67" }).Build();

            Expect(TlsCertificateLoader.ResolveThumbprint(none, file) is null, "no file and no configuration must mean no certificate");

            File.WriteAllText(file, "{\"Security\":{}}");
            Expect(TlsCertificateLoader.ResolveThumbprint(none, file) is null, "a file without Tls must mean no certificate");

            File.WriteAllText(file, "{\"Tls\":{\"Thumbprint\":\"\u200e01:23\"}}");
            Throws(() => TlsCertificateLoader.ResolveThumbprint(none, file));

            File.WriteAllText(file, "{\"Tls\":{\"Thumbprint\":\"  " + "0123456789abcdef0123456789abcdef01234567" + "  \"}}");
            Expect(TlsCertificateLoader.ResolveThumbprint(none, file) == Thumbprint,
                "the settings file thumbprint was not normalised");

            Expect(TlsCertificateLoader.ResolveThumbprint(configured, file) == Thumbprint,
                "configuration must win and spaces must be ignored");

            File.WriteAllText(file, "{\"Tls\":[]}");
            Expect(TlsCertificateLoader.ResolveThumbprint(none, file) is null, "a malformed section is not a certificate");

            Throws(() => TlsCertificateLoader.NormalizeThumbprint("not a thumbprint"));
            Throws(() => TlsCertificateLoader.NormalizeThumbprint(Thumbprint + "00"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        Console.WriteLine("HTTPS certificate thumbprint resolution checks passed.");
    }

    private static void Throws(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("TLS certificate check failed: expected the value to be refused.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("TLS certificate check failed: " + message + ".");
        }
    }
}
