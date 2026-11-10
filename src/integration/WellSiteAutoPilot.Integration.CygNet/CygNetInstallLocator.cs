using Microsoft.Win32;

namespace WellSiteAutoPilot.Integration.CygNet;

/// <summary>
/// Finds the CygNet API libraries already installed on this machine. WSA never installs, registers or ships CygNet: a server that
/// can reach a CygNet domain already has CygNet (or its client utilities) set up, and that setup tells us where the libraries are.
/// </summary>
public static class CygNetInstallLocator
{
    public const string LibraryPathVariable = "CYGNET_LIBRARY_PATH";
    public const string MarkerAssembly = "CygNet.API.Facilities.dll";

    // A CygNet machine registers its COM libraries; the folder of any of them is the CygNet library folder.
    private const string ComProgId = "CxCvsLib.CvsClient";

/// <summary>The CygNet library folder: the Gateway's <c>CygNet:LibraryPath</c> setting, else <c>CYGNET_LIBRARY_PATH</c>, else the folder CygNet registered, else <c>C:\CygNet\Support64</c> / <c>Support</c>; the first that holds the CygNet API.</summary>
    public static string? Locate() => Locate(Environment.GetEnvironmentVariable, RegisteredComFolder, File.Exists, WellKnownFolders, ConfiguredFolder);

    /// <summary>Set from the Gateway's <c>CygNet:LibraryPath</c> setting; tried before everything else.</summary>
    public static string? ConfiguredFolder { get; set; }

    // CygNet's default install location, used when nothing is set and nothing is registered.
    private static readonly string[] WellKnownFolders = [@"C:\CygNet\Support64", @"C:\CygNet\Support"];

    public static string? Locate(Func<string, string?> environment, Func<string?> registeredFolder, Func<string, bool> fileExists,
        IEnumerable<string>? wellKnownFolders = null, string? configuredFolder = null)
    {
        foreach (var candidate in new[] { configuredFolder, environment(LibraryPathVariable), registeredFolder() }.Concat(wellKnownFolders ?? []))
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            var folder = candidate.Trim().Trim('"');
            if (fileExists(Path.Combine(folder, MarkerAssembly)))
                return folder;
        }
        return null;
    }

    private static string? RegisteredComFolder()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var clsidKey = root.OpenSubKey($@"SOFTWARE\Classes\{ComProgId}\CLSID");
            if (clsidKey?.GetValue(null) is not string clsid)
                return null;
            using var server = root.OpenSubKey($@"SOFTWARE\Classes\CLSID\{clsid}\InprocServer32");
            return server?.GetValue(null) is string path && !string.IsNullOrWhiteSpace(path) ? Path.GetDirectoryName(path) : null;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
