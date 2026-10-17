using Npgsql;
using WellSiteAutoPilot.Dev.Core;

namespace WellSiteAutoPilot.AppHost;

/// <summary>Runs the SQL chosen by <see cref="DatabasePlanner"/> against the installed PostgreSQL.</summary>
internal static class DatabasePreparation
{
    public static async Task RunAsync(DatabaseMode mode, DatabaseTarget target, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(target.MaintenanceConnectionString);
        await connection.OpenAsync(cancellationToken);

        var databaseExists = await ExistsAsync(connection, target.Name, cancellationToken);
        var templateExists = await ExistsAsync(connection, target.TemplateName, cancellationToken);
        var statements = DatabasePlanner.Plan(mode, target, databaseExists, templateExists);

        Console.WriteLine($"Database '{target.Name}' mode {mode}: {statements.Count} statement(s).");
        foreach (var sql in statements)
        {
            Console.WriteLine("  " + sql);
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection connection, string name, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        command.Parameters.AddWithValue("name", name);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }
}
