using Npgsql;
using NpgsqlTypes;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Infrastructure.Persistence;

/// <summary>
/// Bulk operations on int_phone_numbers over unnest(...) arrays - one round trip per 5,000
/// numbers instead of one per number. Runs on the SCOPED context's connection and transaction,
/// so it commits or rolls back together with the stage that called it.
/// </summary>
internal sealed class PhoneNumberRegistry(IntegrationDbContext db, TimeProvider time) : IPhoneNumberRegistry
{
    private const int ChunkSize = 5000;

    public async Task<IReadOnlyDictionary<string, RegistryEntry>> EnsureAsync(
        IReadOnlyCollection<NumberInfo> numbers, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, RegistryEntry>(StringComparer.Ordinal);
        var now = time.GetUtcNow();

        foreach (var chunk in numbers.DistinctBy(n => n.E164).Chunk(ChunkSize))
        {
            await using (var insert = await NpgsqlCommands.CreateAsync(db, """
                INSERT INTO int_phone_numbers
                    (id, e164, digits, country_code, region_code, country_iso, country_name,
                     whatsapp_status, check_count, first_seen_at_utc, updated_at_utc)
                SELECT u.id, u.e164, u.digits, u.country_code, u.region_code, u.country_iso, u.country_name,
                       'Unknown', 0, @now, @now
                FROM unnest(@ids, @e164, @digits, @country_codes, @regions, @isos, @names)
                     AS u(id, e164, digits, country_code, region_code, country_iso, country_name)
                ON CONFLICT (e164) DO NOTHING
                """, cancellationToken))
            {
                insert.Parameters.Add(Array("ids", NpgsqlDbType.Uuid, chunk.Select(_ => Guid.CreateVersion7()).ToArray()));
                insert.Parameters.Add(Array("e164", NpgsqlDbType.Text, chunk.Select(n => n.E164).ToArray()));
                insert.Parameters.Add(Array("digits", NpgsqlDbType.Text, chunk.Select(n => n.Digits).ToArray()));
                insert.Parameters.Add(Array("country_codes", NpgsqlDbType.Text, chunk.Select(n => n.CountryCode).ToArray()));
                insert.Parameters.Add(Array("regions", NpgsqlDbType.Text, chunk.Select(n => n.RegionCode).ToArray()));
                insert.Parameters.Add(Array("isos", NpgsqlDbType.Text, chunk.Select(n => n.CountryIso).ToArray()));
                insert.Parameters.Add(Array("names", NpgsqlDbType.Text, chunk.Select(n => n.CountryName).ToArray()));
                insert.Parameters.AddWithValue("now", now);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var select = await NpgsqlCommands.CreateAsync(db, """
                SELECT id, e164, whatsapp_status, whatsapp_checked_at_utc, last_check_result_id, last_checked_tenant_id
                FROM int_phone_numbers
                WHERE e164 = ANY(@e164)
                """, cancellationToken);
            select.Parameters.Add(Array("e164", NpgsqlDbType.Text, chunk.Select(n => n.E164).ToArray()));

            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var e164 = reader.GetString(1);
                result[e164] = new RegistryEntry(
                    reader.GetGuid(0),
                    e164,
                    Enum.Parse<WhatsAppStatus>(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetGuid(5));
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, SavedAnswer>> GetTenantAnswersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> phoneNumberIds, CancellationToken cancellationToken)
    {
        var answers = new Dictionary<Guid, SavedAnswer>();

        foreach (var chunk in phoneNumberIds.Chunk(ChunkSize))
        {
            await using var command = await NpgsqlCommands.CreateAsync(db, """
                SELECT DISTINCT ON (r.phone_number_id) r.phone_number_id, r.whatsapp_status, r.checked_at_utc, r.id
                FROM int_checknumber_results AS r
                JOIN int_checknumber_tasks AS t ON t.id = r.checknumber_task_id AND NOT t.is_superseded
                WHERE r.tenant_id = @tenant
                  AND r.phone_number_id = ANY(@ids)
                  AND r.whatsapp_status IN ('Yes', 'No')
                ORDER BY r.phone_number_id, r.checked_at_utc DESC
                """, cancellationToken);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.Add(Array("ids", NpgsqlDbType.Uuid, chunk));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var phoneId = reader.GetGuid(0);
                answers[phoneId] = new SavedAnswer(
                    phoneId,
                    Enum.Parse<WhatsAppStatus>(reader.GetString(1)),
                    reader.GetFieldValue<DateTimeOffset>(2),
                    reader.GetGuid(3));
            }
        }

        return answers;
    }

    public async Task ApplyAnswersAsync(
        IReadOnlyCollection<RegistryAnswer> answers,
        Guid tenantId,
        DateTimeOffset checkedAtUtc,
        bool countAsNewCheck,
        CancellationToken cancellationToken)
    {
        foreach (var chunk in answers.Chunk(ChunkSize))
        {
            await using var command = await NpgsqlCommands.CreateAsync(db, """
                UPDATE int_phone_numbers AS p SET
                    whatsapp_status = CASE WHEN u.status IN ('Yes', 'No') THEN u.status ELSE p.whatsapp_status END,
                    whatsapp_checked_at_utc = CASE WHEN u.status IN ('Yes', 'No') THEN @checked ELSE p.whatsapp_checked_at_utc END,
                    last_check_result_id = CASE WHEN u.status IN ('Yes', 'No') THEN u.result_id ELSE p.last_check_result_id END,
                    last_checked_tenant_id = CASE WHEN u.status IN ('Yes', 'No') THEN @tenant ELSE p.last_checked_tenant_id END,
                    check_count = p.check_count + @increment,
                    updated_at_utc = @checked
                FROM unnest(@ids, @statuses, @results) AS u(id, status, result_id)
                WHERE p.id = u.id
                """, cancellationToken);

            command.Parameters.Add(Array("ids", NpgsqlDbType.Uuid, chunk.Select(a => a.PhoneNumberId).ToArray()));
            command.Parameters.Add(Array("statuses", NpgsqlDbType.Text, chunk.Select(a => a.Status.ToString()).ToArray()));
            command.Parameters.Add(Array("results", NpgsqlDbType.Uuid, chunk.Select(a => a.ResultId).ToArray()));
            command.Parameters.AddWithValue("checked", checkedAtUtc);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("increment", countAsNewCheck ? 1 : 0);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static NpgsqlParameter Array<T>(string name, NpgsqlDbType elementType, T[] values) =>
        new(name, NpgsqlDbType.Array | elementType) { Value = values };
}
