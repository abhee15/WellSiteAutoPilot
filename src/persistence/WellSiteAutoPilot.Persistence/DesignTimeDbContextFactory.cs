using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WellSiteAutoPilot.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WellSiteAutoPilotDbContext>
{
    private const string ConnectionStringEnvironmentVariable = "WSA_DATABASE_CONNECTION_STRING";

    public WellSiteAutoPilotDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionStringEnvironmentVariable} before running EF Core design-time commands.");
        }

        var options = new DbContextOptionsBuilder<WellSiteAutoPilotDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new WellSiteAutoPilotDbContext(options);
    }
}
