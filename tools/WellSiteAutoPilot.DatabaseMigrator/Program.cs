using Microsoft.EntityFrameworkCore;
using WellSiteAutoPilot.Persistence;

const string connectionStringVariable = "WSA_DATABASE_CONNECTION_STRING";

var connectionString = Environment.GetEnvironmentVariable(connectionStringVariable);

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine($"Set {connectionStringVariable} before running database migrations.");
    return 2;
}

var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var db = new WellSiteAutoPilotDbContext(options);

var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();

if (pending.Length == 0)
{
    Console.WriteLine("Database schema is current. No pending migrations.");
    return 0;
}

Console.WriteLine($"Applying {pending.Length} migration(s)...");
await db.Database.MigrateAsync();
Console.WriteLine("Database migration completed successfully.");

return 0;
