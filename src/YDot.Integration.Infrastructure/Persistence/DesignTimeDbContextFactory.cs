using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace YDot.Integration.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF Core tools (dotnet ef migrations add / database update):
///
///   dotnet ef migrations add &lt;Name&gt; --project src/YDot.Integration.Infrastructure --output-dir Persistence/Migrations
///
/// The connection string comes from YDOT_INTEGRATION_CONNECTION, falling back to the Docker
/// database as reached from the host (port 6710).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<IntegrationDbContext>
{
    public IntegrationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("YDOT_INTEGRATION_CONNECTION")
                               ?? "Host=localhost;Port=6710;Database=ydotphaseupdated;Username=postgres;Password=user";

        var options = new DbContextOptionsBuilder<IntegrationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(IntegrationDbContext.MigrationsHistoryTable))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IntegrationDbContext(options);
    }
}
