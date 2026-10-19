using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace WellSiteAutoPilot.Server;

/// <summary>
/// Finds the HTTPS certificate the installer chose. The installer puts the certificate in the Local
/// Computer "Personal" store and records only its thumbprint (Tls:Thumbprint) in the protected machine
/// settings file, so no certificate secret is stored in configuration. Without a thumbprint the Server
/// serves whatever addresses it is given (plain HTTP in development).
/// </summary>
public static class TlsCertificateLoader
{
    public const string SectionName = "Tls";
    public const string ThumbprintName = "Thumbprint";

    private static readonly byte[] Probe = [1, 2, 3];

    /// <summary>Configuration wins over the settings file. Null means no certificate is configured.</summary>
    public static string? ResolveThumbprint(IConfiguration configuration, string settingsPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        var configured = configuration[$"{SectionName}:{ThumbprintName}"];
        return !string.IsNullOrWhiteSpace(configured)
            ? NormalizeThumbprint(configured)
            : ReadSettingsFile(settingsPath);
    }

    public static string? ReadSettingsFile(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty(SectionName, out var section) ||
            section.ValueKind != JsonValueKind.Object ||
            !section.TryGetProperty(ThumbprintName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : NormalizeThumbprint(text);
    }

    /// <summary>Upper-case hex without spaces or the hidden marks Windows adds when copying a thumbprint.</summary>
    public static string NormalizeThumbprint(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var cleaned = new string(value
            .Where(character => !char.IsWhiteSpace(character) && character is not ('‎' or '‏' or '‪' or '‬'))
            .ToArray()).ToUpperInvariant();
        if (cleaned.Length != 40 || !cleaned.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{ThumbprintName} must be a 40 character hexadecimal certificate thumbprint.");
        }

        return cleaned;
    }

    /// <summary>
    /// Loads the certificate and proves the Server's account can use its private key, so a missing
    /// certificate or key permission stops the service at startup with a clear reason.
    /// </summary>
    public static X509Certificate2 Load(string thumbprint)
    {
        var normalized = NormalizeThumbprint(thumbprint);

        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, normalized, validOnly: false);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"The HTTPS certificate with thumbprint {normalized} is not in the Local Computer 'Personal' store. " +
                "Re-run the installer and choose a certificate.");
        }

        var certificate = matches[0];
        for (var index = 1; index < matches.Count; index++)
        {
            matches[index].Dispose();
        }

        ProveKeyIsUsable(certificate, normalized);
        return certificate;
    }

    private static void ProveKeyIsUsable(X509Certificate2 certificate, string thumbprint)
    {
        try
        {
            using var rsa = certificate.GetRSAPrivateKey();
            if (rsa is not null)
            {
                _ = rsa.SignData(Probe, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return;
            }

            using var ecdsa = certificate.GetECDsaPrivateKey();
            if (ecdsa is not null)
            {
                _ = ecdsa.SignData(Probe, HashAlgorithmName.SHA256);
                return;
            }
        }
        catch (CryptographicException exception)
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                $"The HTTPS certificate {thumbprint} is present but its private key cannot be used by the Server's " +
                "account. Re-run the installer, or allow NETWORK SERVICE to read the key " +
                "(certlm.msc > All Tasks > Manage Private Keys).",
                exception);
        }

        certificate.Dispose();
        throw new InvalidOperationException($"The HTTPS certificate {thumbprint} has no private key the Server can use.");
    }
}
