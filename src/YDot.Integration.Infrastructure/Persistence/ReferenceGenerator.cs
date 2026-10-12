using System.Globalization;
using Microsoft.EntityFrameworkCore;
using YDot.Integration.Application.Abstractions.Persistence;

namespace YDot.Integration.Infrastructure.Persistence;

/// <summary>
/// INT-20261010-000001, WAT-20261010-000001 ...: a per-prefix, per-day counter in
/// int_job_sequences, advanced by one atomic INSERT ... ON CONFLICT DO UPDATE ... RETURNING, so
/// concurrent worker instances can never be handed the same number. It runs on its own connection:
/// a number taken by a registration that then fails is simply skipped (a gap is harmless, a
/// duplicate would not be).
/// </summary>
internal sealed class ReferenceGenerator(IDbContextFactory<IntegrationDbContext> contexts, TimeProvider time) : IReferenceGenerator
{
    public async Task<string> NextAsync(string prefix, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var period = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var command = await NpgsqlCommands.CreateAsync(context, """
            INSERT INTO int_job_sequences (name, period, last_value, updated_at_utc)
            VALUES (@name, @period, 1, @now)
            ON CONFLICT (name, period)
            DO UPDATE SET last_value = int_job_sequences.last_value + 1, updated_at_utc = @now
            RETURNING last_value
            """, cancellationToken);

        command.Parameters.AddWithValue("name", prefix);
        command.Parameters.AddWithValue("period", period);
        command.Parameters.AddWithValue("now", now);

        var value = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return $"{prefix}-{period}-{value:D6}";
    }
}
