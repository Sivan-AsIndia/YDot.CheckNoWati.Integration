using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Options;

namespace YDot.Integration.Infrastructure.Persistence;

/// <summary>
/// Creates / upgrades the int_* tables at start-up (Database:ApplyMigrationsOnStartup), waiting
/// for PostgreSQL to come up first - in Docker the worker may start before the database is ready.
/// A migration that fails stops the worker: running against a schema it could not migrate would
/// fail later, in scattered ways, in the middle of a paid job.
/// </summary>
public sealed class DatabaseMigrator(
    IDbContextFactory<IntegrationDbContext> contexts,
    IOptions<DatabaseOptions> options,
    TimeProvider time,
    ILogger<DatabaseMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var deadline = time.GetUtcNow().AddSeconds(settings.StartupConnectTimeoutSeconds);
        var attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                await using var context = await contexts.CreateDbContextAsync(cancellationToken);

                if (!settings.ApplyMigrationsOnStartup)
                {
                    await context.Database.CanConnectAsync(cancellationToken);
                    var pendingNow = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                    if (pendingNow.Count > 0)
                    {
                        logger.LogWarning("Database:ApplyMigrationsOnStartup is off and {Count} migration(s) are pending: {Migrations}",
                            pendingNow.Count, string.Join(", ", pendingNow));
                    }

                    return;
                }

                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (pending.Count == 0)
                {
                    logger.LogInformation("Integration database schema (int_*) is up to date.");
                    return;
                }

                logger.LogInformation("Applying {Count} migration(s) to the integration schema: {Migrations}", pending.Count, string.Join(", ", pending));
                await context.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Integration database schema migrated.");
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException && time.GetUtcNow() < deadline && IsConnectionProblem(exception))
            {
                logger.LogWarning("The database is not reachable yet (attempt {Attempt}): {Reason}. Trying again in 5 seconds.", attempt, exception.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), time, cancellationToken);
            }
        }
    }

    private static bool IsConnectionProblem(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is Npgsql.NpgsqlException { IsTransient: true } or System.Net.Sockets.SocketException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }
}
