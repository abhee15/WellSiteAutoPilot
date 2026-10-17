namespace WellSiteAutoPilot.Server;

/// <summary>
/// Chooses the folder the Server serves the built frontend from. Production always uses
/// the packaged <c>wwwroot</c>. Only the Development environment may point at another
/// folder (for example <c>src/web/dist</c>) so local Windows-authentication testing does
/// not need the build output copied into <c>wwwroot</c>.
/// </summary>
public static class FrontendStaticRoot
{
    public static string Resolve(string? environmentName, string? configuredRoot, string defaultRoot)
    {
        if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(configuredRoot))
        {
            return defaultRoot;
        }

        var fullPath = Path.GetFullPath(configuredRoot.Trim());
        // The folder may not exist yet when the frontend watch build has not produced output;
        // files created later are still served because static files are read on each request.
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }
}
