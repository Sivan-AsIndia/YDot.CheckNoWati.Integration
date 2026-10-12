using System.Text;
using Npgsql;
using YDot.Integration.Application.Abstractions.Persistence;

namespace YDot.Integration.Infrastructure.Persistence;

/// <summary>
/// A cluster-wide lock on a NAME, using PostgreSQL session advisory locks
/// (pg_try_advisory_lock / pg_advisory_unlock) on a dedicated connection. The lock disappears by
/// itself if the process dies, because it belongs to the connection.
/// </summary>
internal sealed class PostgresAdvisoryLock(NpgsqlDataSource dataSource) : IDistributedLock
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken cancellationToken)
    {
        var key = StableKey(name);
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", key);

            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                return new Handle(connection, key);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    /// <summary>FNV-1a 64-bit of the name: the same name gives the same key on every instance.</summary>
    private static long StableKey(string name)
    {
        var hash = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(name))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return unchecked((long)hash);
    }

    private sealed class Handle(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteScalarAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
