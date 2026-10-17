using System.Data.Common;
using System.Text.RegularExpressions;

namespace WellSiteAutoPilot.Dev.Core;

/// <summary>How the local database is prepared before the services start.</summary>
public enum DatabaseMode
{
    /// <summary>Create the database if it is missing; keep any existing data.</summary>
    Reuse,
    /// <summary>Drop and recreate the database. Destructive.</summary>
    Reset,
    /// <summary>Drop the database and recreate it from the saved template. Destructive.</summary>
    FromTemplate,
    /// <summary>Save the current database as the template, then continue with it unchanged.</summary>
    SaveTemplate
}

public sealed record DatabaseTarget(
    string Host,
    string Name,
    string TemplateName,
    string MaintenanceConnectionString,
    string DatabaseConnectionString);

public static partial class DatabaseSettings
{
    public const string NameRule = "wsa_ followed by 1 to 50 lowercase letters, digits or underscores";

    [GeneratedRegex("^wsa_[a-z0-9_]{1,50}$")]
    private static partial Regex NamePattern();

    public static DatabaseMode ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DatabaseMode.Reuse;
        }

        if (Enum.TryParse<DatabaseMode>(value.Trim(), ignoreCase: true, out var mode) && Enum.IsDefined(mode))
        {
            return mode;
        }

        throw new InvalidOperationException(
            $"Database:Mode '{value}' is not valid. Use Reuse, Reset, FromTemplate or SaveTemplate.");
    }

    public static DatabaseTarget Parse(string connectionString, string? templateName, string? password)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var host = Read(builder, "Host", "Server")
            ?? throw new InvalidOperationException("Database:ConnectionString needs a Host.");
        var name = Read(builder, "Database", "Initial Catalog")
            ?? throw new InvalidOperationException("Database:ConnectionString needs a Database name.");
        RequireName(name);

        var template = string.IsNullOrWhiteSpace(templateName) ? name + "_template" : templateName.Trim();
        RequireName(template);

        builder.Remove("Initial Catalog");
        if (!string.IsNullOrEmpty(password))
        {
            builder["Password"] = password;
        }

        builder["Database"] = name;
        var database = builder.ConnectionString;
        builder["Database"] = "postgres";
        return new DatabaseTarget(host, name, template, builder.ConnectionString, database);
    }

    /// <summary>Destructive modes only ever touch a local server.</summary>
    public static void RequireLocalHost(string host)
    {
        var allowed = new[] { "localhost", "127.0.0.1", "::1", "[::1]" };
        if (!allowed.Contains(host.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Refusing a destructive database mode against host '{host}'. Only a local server is allowed.");
        }
    }

    public static void RequireName(string name)
    {
        if (!NamePattern().IsMatch(name))
        {
            throw new InvalidOperationException($"Database name '{name}' must be {NameRule}.");
        }
    }

    private static string? Read(DbConnectionStringBuilder builder, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return null;
    }
}
