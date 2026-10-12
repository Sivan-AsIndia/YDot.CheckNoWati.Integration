using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Options;

namespace YDot.Integration.Infrastructure.Persistence;

/// <summary>
/// Claims rows for this worker instance with one atomic statement each:
///
///   UPDATE ... SET locked_by = me, locked_until_utc = now + lease
///   WHERE id IN (SELECT id ... WHERE due AND (unlocked OR lease expired)
///                ORDER BY ... LIMIT n FOR UPDATE SKIP LOCKED)
///   RETURNING id
///
/// SKIP LOCKED is what lets several instances poll the same table at the same moment without
/// waiting on each other or ever taking the same row. An expired lease (its instance crashed)
/// makes the row claimable again; that takeover is counted in processing_attempts.
/// </summary>
internal sealed class LeaseStore(
    IDbContextFactory<IntegrationDbContext> contexts,
    IWorkerIdentity identity,
    IOptions<WorkerOptions> options,
    TimeProvider time) : ILeaseStore
{
    private const string TerminalJobStatuses = "'Completed','CompletedWithErrors','Failed','Cancelled'";

    private int LeaseSeconds => options.Value.JobLeaseSeconds;

    public Task<IReadOnlyList<Guid>> ClaimIntegrationJobsAsync(int maximum, CancellationToken cancellationToken) =>
        ClaimAsync($"""
            UPDATE int_integration_jobs AS j
            SET locked_by = @instance,
                locked_until_utc = @until,
                processing_attempts = j.processing_attempts
                    + CASE WHEN j.locked_by IS NOT NULL AND j.locked_by <> @instance THEN 1 ELSE 0 END
            WHERE j.id IN (
                SELECT id FROM int_integration_jobs
                WHERE status NOT IN ({TerminalJobStatuses})
                  AND (locked_until_utc IS NULL OR locked_until_utc < @now)
                  AND (cancel_requested OR next_attempt_at_utc IS NULL OR next_attempt_at_utc <= @now)
                ORDER BY cancel_requested DESC, COALESCE(next_attempt_at_utc, created_at_utc), created_at_utc
                LIMIT @maximum
                FOR UPDATE SKIP LOCKED)
            RETURNING j.id
            """, maximum, cancellationToken);

    public Task<bool> RenewIntegrationJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        RenewAsync("int_integration_jobs", jobId, cancellationToken);

    public Task ReleaseIntegrationJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        ReleaseAsync("int_integration_jobs", jobId, cancellationToken);

    public Task<IReadOnlyList<Guid>> ClaimWatiJobsAsync(int maximum, CancellationToken cancellationToken) =>
        ClaimAsync("""
            UPDATE int_wati_jobs AS w
            SET locked_by = @instance, locked_until_utc = @until
            WHERE w.id IN (
                SELECT id FROM int_wati_jobs
                WHERE status IN ('Queued', 'Sending')
                  AND (locked_until_utc IS NULL OR locked_until_utc < @now)
                  AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= @now)
                ORDER BY created_at_utc
                LIMIT @maximum
                FOR UPDATE SKIP LOCKED)
            RETURNING w.id
            """, maximum, cancellationToken);

    public Task<bool> RenewWatiJobAsync(Guid watiJobId, CancellationToken cancellationToken) =>
        RenewAsync("int_wati_jobs", watiJobId, cancellationToken);

    public Task ReleaseWatiJobAsync(Guid watiJobId, CancellationToken cancellationToken) =>
        ReleaseAsync("int_wati_jobs", watiJobId, cancellationToken);

    public Task<IReadOnlyList<Guid>> ClaimCommandsAsync(int maximum, CancellationToken cancellationToken) =>
        ClaimAsync("""
            UPDATE int_worker_commands AS c
            SET status = 'Processing', locked_by = @instance, locked_until_utc = @until, attempts = c.attempts + 1
            WHERE c.id IN (
                SELECT id FROM int_worker_commands
                WHERE (status = 'Pending' AND (locked_until_utc IS NULL OR locked_until_utc <= @now))
                   OR (status = 'Processing' AND locked_until_utc < @now)
                ORDER BY requested_at_utc
                LIMIT @maximum
                FOR UPDATE SKIP LOCKED)
            RETURNING c.id
            """, maximum, cancellationToken);

    private async Task<IReadOnlyList<Guid>> ClaimAsync(string sql, int maximum, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var command = await NpgsqlCommands.CreateAsync(context, sql, cancellationToken);
        var now = time.GetUtcNow();

        command.Parameters.AddWithValue("instance", identity.InstanceName);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("until", now.AddSeconds(LeaseSeconds));
        command.Parameters.AddWithValue("maximum", maximum);

        return await NpgsqlCommands.ReadGuidsAsync(command, cancellationToken);
    }

    private async Task<bool> RenewAsync(string table, Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var command = await NpgsqlCommands.CreateAsync(context,
            $"UPDATE {table} SET locked_until_utc = @until WHERE id = @id AND locked_by = @instance", cancellationToken);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("instance", identity.InstanceName);
        command.Parameters.AddWithValue("until", time.GetUtcNow().AddSeconds(LeaseSeconds));

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task ReleaseAsync(string table, Guid id, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var command = await NpgsqlCommands.CreateAsync(context,
            $"UPDATE {table} SET locked_by = NULL, locked_until_utc = NULL WHERE id = @id AND locked_by = @instance", cancellationToken);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("instance", identity.InstanceName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
