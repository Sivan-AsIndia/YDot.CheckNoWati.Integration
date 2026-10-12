using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using YDot.Integration.Application.Abstractions.Persistence;

namespace YDot.Integration.Infrastructure.Persistence;

/// <summary>
/// Reads organisations from IAM's iam_tenants table in the same database. Read-only: the worker
/// never writes an IAM table. When the worker runs against a database without IAM (a standalone
/// test database), every lookup answers "not found" instead of failing.
/// </summary>
internal sealed class TenantDirectory(IDbContextFactory<IntegrationDbContext> contexts, ILogger<TenantDirectory> logger) : ITenantDirectory
{
    private const string UndefinedTable = "42P01";

    public Task<TenantInfo?> FindAsync(string subdomainOrCode, CancellationToken cancellationToken) =>
        QueryAsync(
            "SELECT id, code, name, subdomain, status FROM iam_tenants WHERE lower(subdomain) = lower(@value) OR lower(code) = lower(@value) LIMIT 1",
            command => command.Parameters.AddWithValue("value", subdomainOrCode.Trim()),
            cancellationToken);

    public Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
        QueryAsync(
            "SELECT id, code, name, subdomain, status FROM iam_tenants WHERE id = @id",
            command => command.Parameters.AddWithValue("id", tenantId),
            cancellationToken);

    private async Task<TenantInfo?> QueryAsync(string sql, Action<NpgsqlCommand> bind, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await contexts.CreateDbContextAsync(cancellationToken);
            await using var command = await NpgsqlCommands.CreateAsync(context, sql, cancellationToken);
            bind(command);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new TenantInfo(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
        }
        catch (PostgresException exception) when (exception.SqlState == UndefinedTable)
        {
            logger.LogWarning("iam_tenants does not exist in this database; organisations cannot be looked up.");
            return null;
        }
    }
}
