using System.Security.Cryptography;
using System.Text;

namespace WellSiteAutoPilot.Http.ServiceCallers;

/// <summary>
/// Keys that internal callers (the Server, the worker) present to the Integration Gateway. A key is 256
/// random bits, so the Gateway needs to keep only a SHA-256 verifier of it: whoever can read the verifier
/// learns nothing they can call with.
/// </summary>
public static class ServiceCallerKeys
{
    public static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static string VerifierFor(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    /// <summary>Constant-time check of a presented key against a stored verifier; false for any malformed input.</summary>
    public static bool Matches(string? presentedKey, string? verifier)
    {
        if (string.IsNullOrEmpty(presentedKey) || string.IsNullOrEmpty(verifier)) return false;
        try
        {
            var expected = Convert.FromBase64String(verifier);
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey));
            return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
