using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

/// <summary>
/// Puts the Server's HTTPS certificate in LocalMachine\My and lets the Server's account use its private
/// key. Whatever the source (a certificate already in the store, a PFX file, or a generated self-signed
/// certificate), the outcome is the same: a thumbprint the Server loads from the store at startup.
/// Nothing secret is written to the settings file.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ServerCertificate
{
    private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    private const string NetworkServiceSid = "*S-1-5-20";

    public static ProvisionedCertificate Provision(
        CertificateChoice choice,
        string? storedSource,
        string? pfxPassword)
    {
        ArgumentNullException.ThrowIfNull(choice);

        var warnings = new List<string>();
        X509Certificate2 certificate;
        string source;

        switch (choice.Kind)
        {
            case CertificateKind.Store:
                certificate = FindInStore(choice.Value!)
                              ?? throw new InvalidOperationException(NotFoundMessage(choice.Value!));
                source = "Store";
                break;

            case CertificateKind.Pfx:
                certificate = ImportPfx(choice.Value!, pfxPassword);
                source = "Pfx";
                break;

            case CertificateKind.SelfSigned:
                certificate = CreateSelfSigned();
                source = "SelfSigned";
                warnings.Add(
                    "A self-signed certificate is for trial use: browsers warn until it is trusted. " +
                    "Install a certificate from your organisation's authority for production.");
                break;

            case CertificateKind.Stored:
                var stored = FindInStore(choice.Value!);
                if (stored is null && string.Equals(storedSource, "SelfSigned", StringComparison.Ordinal))
                {
                    // The generated certificate is gone from the store; make another rather than fail.
                    certificate = CreateSelfSigned();
                    source = "SelfSigned";
                    warnings.Add("The earlier self-signed certificate was missing, so a new one was created.");
                }
                else
                {
                    certificate = stored ?? throw new InvalidOperationException(NotFoundMessage(choice.Value!));
                    source = storedSource ?? "Store";
                }

                break;

            default:
                throw new InvalidOperationException("Unknown certificate source.");
        }

        using (certificate)
        {
            Validate(certificate, warnings);
            GrantNetworkServiceKeyAccess(certificate, warnings);
            return new ProvisionedCertificate(
                certificate.Thumbprint.ToUpperInvariant(),
                source,
                certificate.Subject,
                certificate.NotAfter,
                warnings);
        }
    }

    private static X509Certificate2? FindInStore(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        return found.Count == 0 ? null : found[0];
    }

    private static string NotFoundMessage(string thumbprint)
    {
        var available = new StringBuilder();
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        foreach (var candidate in store.Certificates)
        {
            if (candidate.HasPrivateKey)
            {
                available.Append(CultureInfo.InvariantCulture,
                    $"{Environment.NewLine}  {candidate.Thumbprint}  {candidate.Subject}  expires {candidate.NotAfter:yyyy-MM-dd}");
            }

            candidate.Dispose();
        }

        return $"No certificate with thumbprint {thumbprint} is in the Local Computer 'Personal' store. " +
               "Import it there first (it must include its private key). Certificates with a private key there now:" +
               (available.Length == 0 ? " none." : available.ToString());
    }

    private static X509Certificate2 ImportPfx(string path, string? password)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"The certificate file '{path}' was not found.");
        }

        X509Certificate2 certificate;
        try
        {
            // MachineKeySet keeps the key where the Server's service account can be granted access;
            // PersistKeySet keeps it after this process exits.
            certificate = X509CertificateLoader.LoadPkcs12FromFile(
                path,
                password,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                $"The certificate file '{path}' could not be opened. Check the file and its password " +
                $"(supply the password in the {InstallerSettings.PfxPasswordVariable} environment variable): {exception.Message}",
                exception);
        }

        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"The certificate file '{path}' has no private key.");
        }

        AddToStore(certificate);
        return certificate;
    }

    private static X509Certificate2 CreateSelfSigned()
    {
        var names = new List<string> { Environment.MachineName.ToUpperInvariant(), "localhost" };
        try
        {
            var fullName = Dns.GetHostEntry(string.Empty).HostName.ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(fullName) && !names.Contains(fullName, StringComparer.OrdinalIgnoreCase))
            {
                names.Insert(1, fullName);
            }
        }
        catch (Exception exception) when (exception is System.Net.Sockets.SocketException or ArgumentException)
        {
            // The short name is enough when the domain name cannot be resolved.
        }

        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN={names[0]}, O=WellSite AutoPilot (self-signed)"),
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var usages = new OidCollection { new Oid(ServerAuthenticationOid) };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, false));

        var alternativeNames = new SubjectAlternativeNameBuilder();
        foreach (var name in names)
        {
            alternativeNames.AddDnsName(name);
        }

        alternativeNames.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(alternativeNames.Build());

        using var created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));

        // Round-trip through a PFX so the key is persisted in the machine key store.
        var temporaryPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var pfx = created.Export(X509ContentType.Pfx, temporaryPassword);
        var certificate = X509CertificateLoader.LoadPkcs12(
            pfx,
            temporaryPassword,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
        certificate.FriendlyName = "WellSite AutoPilot (self-signed)";
        AddToStore(certificate);
        return certificate;
    }

    private static void AddToStore(X509Certificate2 certificate)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        store.Add(certificate);
    }

    private static void Validate(X509Certificate2 certificate, List<string> warnings)
    {
        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException(
                $"Certificate {certificate.Thumbprint} has no private key in this computer's store, so the Server cannot use it.");
        }

        var now = DateTime.Now;
        if (certificate.NotAfter < now || certificate.NotBefore > now)
        {
            throw new InvalidOperationException(
                $"Certificate {certificate.Thumbprint} is not valid today (valid {certificate.NotBefore:yyyy-MM-dd} to {certificate.NotAfter:yyyy-MM-dd}).");
        }

        foreach (var extension in certificate.Extensions)
        {
            if (extension is X509EnhancedKeyUsageExtension usage &&
                !usage.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == ServerAuthenticationOid))
            {
                throw new InvalidOperationException(
                    $"Certificate {certificate.Thumbprint} is not marked for server authentication.");
            }
        }

        if (certificate.NotAfter < now.AddDays(30))
        {
            warnings.Add($"The certificate expires on {certificate.NotAfter:yyyy-MM-dd}; renew it soon.");
        }

        var machine = Environment.MachineName;
        var names = certificate.GetNameInfo(X509NameType.DnsName, false) ?? string.Empty;
        var covered = names.Contains(machine, StringComparison.OrdinalIgnoreCase) ||
                      names.StartsWith('*') ||
                      certificate.Extensions.Cast<X509Extension>().Any(extension =>
                          extension.Oid?.Value == "2.5.29.17" &&
                          extension.Format(false).Contains(machine, StringComparison.OrdinalIgnoreCase));
        if (!covered)
        {
            warnings.Add(
                $"The certificate does not appear to name this computer ({machine}). Browsers that use that name will warn.");
        }
    }

    /// <summary>
    /// The Server runs as NetworkService, which cannot read a machine private key that only
    /// Administrators and SYSTEM may read. Grants it read access to that one key file.
    /// </summary>
    private static void GrantNetworkServiceKeyAccess(X509Certificate2 certificate, List<string> warnings)
    {
        var keyName = FindKeyName(certificate);
        if (keyName is null)
        {
            warnings.Add(
                "The certificate's private key is not a file-based key (for example a smart card or hardware module), " +
                "so key access was not changed. The Server's account needs to be allowed to use it.");
            return;
        }

        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var directories = new[]
        {
            Path.Combine(common, "Microsoft", "Crypto", "Keys"),
            Path.Combine(common, "Microsoft", "Crypto", "RSA", "MachineKeys"),
            Path.Combine(common, "Microsoft", "Crypto", "SystemKeys")
        };

        foreach (var directory in directories)
        {
            var keyFile = Path.Combine(directory, keyName);
            if (!File.Exists(keyFile))
            {
                continue;
            }

            var result = RunIcacls(keyFile, "/grant", NetworkServiceSid + ":R");
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to let the Server's account read the certificate's private key: {result.Output}");
            }

            return;
        }

        warnings.Add(
            $"The certificate's private key file '{keyName}' was not found in the machine key folders. " +
            "If the Server cannot start, allow NETWORK SERVICE to read the key (certlm.msc > All Tasks > Manage Private Keys).");
    }

    private static string? FindKeyName(X509Certificate2 certificate)
    {
        using (var rsa = certificate.GetRSAPrivateKey())
        {
            switch (rsa)
            {
                case RSACng cng:
                    return cng.Key.UniqueName;
                case RSACryptoServiceProvider csp:
                    return csp.CspKeyContainerInfo.UniqueKeyContainerName;
            }
        }

        using var ecdsa = certificate.GetECDsaPrivateKey();
        return ecdsa is ECDsaCng ecdsaCng ? ecdsaCng.Key.UniqueName : null;
    }

    private static (int ExitCode, string Output) RunIcacls(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "icacls.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("icacls.exe could not be started.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, $"{output} {error}".Trim());
    }
}

internal sealed record ProvisionedCertificate(
    string Thumbprint,
    string Source,
    string Subject,
    DateTime NotAfter,
    IReadOnlyList<string> Warnings);
