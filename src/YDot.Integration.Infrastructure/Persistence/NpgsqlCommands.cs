using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace YDot.Integration.Infrastructure.Persistence;

/// <summary>
/// Runs hand-written SQL on a context's own connection (and inside its current transaction, if
/// any), for the few statements EF cannot express: UPDATE ... FOR UPDATE SKIP LOCKED ... RETURNING,
/// INSERT ... ON CONFLICT, and bulk statements over unnest(...) arrays.
/// </summary>
internal static class NpgsqlCommands
{
    public static async Task<NpgsqlCommand> CreateAsync(DbContext context, string sql, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        var timeout = context.Database.GetCommandTimeout();
        if (timeout is { } seconds)
        {
            command.CommandTimeout = seconds;
        }

        return command;
    }

    public static async Task<List<Guid>> ReadGuidsAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }
}
