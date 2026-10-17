namespace WellSiteAutoPilot.Dev.Core;

public static class DatabasePlanner
{
    /// <summary>Returns the ordered SQL for a mode. Pure, so it is testable without a database.</summary>
    public static IReadOnlyList<string> Plan(
        DatabaseMode mode,
        DatabaseTarget target,
        bool databaseExists,
        bool templateExists)
    {
        DatabaseSettings.RequireName(target.Name);
        DatabaseSettings.RequireName(target.TemplateName);
        var name = Quote(target.Name);
        var template = Quote(target.TemplateName);

        switch (mode)
        {
            case DatabaseMode.Reuse:
                return databaseExists ? [] : [$"CREATE DATABASE {name}"];

            case DatabaseMode.Reset:
                DatabaseSettings.RequireLocalHost(target.Host);
                return [$"DROP DATABASE IF EXISTS {name} WITH (FORCE)", $"CREATE DATABASE {name}"];

            case DatabaseMode.FromTemplate:
                DatabaseSettings.RequireLocalHost(target.Host);
                if (!templateExists)
                {
                    throw new InvalidOperationException(
                        $"Template database '{target.TemplateName}' does not exist. " +
                        "Run once with Database:Mode set to SaveTemplate to create it.");
                }

                return [$"DROP DATABASE IF EXISTS {name} WITH (FORCE)", $"CREATE DATABASE {name} TEMPLATE {template}"];

            case DatabaseMode.SaveTemplate:
                DatabaseSettings.RequireLocalHost(target.Host);
                if (!databaseExists)
                {
                    throw new InvalidOperationException(
                        $"Database '{target.Name}' does not exist, so there is nothing to save as a template.");
                }

                return [$"DROP DATABASE IF EXISTS {template} WITH (FORCE)", $"CREATE DATABASE {template} TEMPLATE {name}"];

            default:
                throw new InvalidOperationException($"Unsupported database mode '{mode}'.");
        }
    }

    // Names are validated by DatabaseSettings.RequireName before they reach this point.
    private static string Quote(string name) => "\"" + name + "\"";
}
