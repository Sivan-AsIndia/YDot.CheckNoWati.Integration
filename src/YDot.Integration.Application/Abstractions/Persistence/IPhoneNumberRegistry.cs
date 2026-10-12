using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Abstractions.Persistence;

/// <summary>Everything known about a normalised number before it is registered.</summary>
public sealed record NumberInfo(
    string E164,
    string Digits,
    string CountryCode,
    string? RegionCode,
    string? CountryIso,
    string? CountryName);

/// <summary>A number's registry row as it stands now.</summary>
public sealed record RegistryEntry(
    Guid Id,
    string E164,
    WhatsAppStatus Status,
    DateTimeOffset? CheckedAtUtc,
    Guid? LastResultId,
    Guid? LastCheckedTenantId);

/// <summary>A definitive answer (Yes or No) one organisation already paid for.</summary>
public sealed record SavedAnswer(Guid PhoneNumberId, WhatsAppStatus Status, DateTimeOffset CheckedAtUtc, Guid ResultId);

/// <summary>A fresh CheckNumber.ai answer to write into the registry.</summary>
public sealed record RegistryAnswer(Guid PhoneNumberId, WhatsAppStatus Status, Guid ResultId);

/// <summary>
/// Bulk operations on the number registry (int_phone_numbers), done in SQL because a file can
/// hold tens of thousands of numbers and row-by-row EF inserts would take minutes.
/// </summary>
public interface IPhoneNumberRegistry
{
    /// <summary>
    /// Inserts the numbers that are not in the registry yet (INSERT ... ON CONFLICT DO NOTHING, so
    /// two workers registering the same number at once is harmless) and returns every row, keyed
    /// by E.164.
    /// </summary>
    Task<IReadOnlyDictionary<string, RegistryEntry>> EnsureAsync(
        IReadOnlyCollection<NumberInfo> numbers, CancellationToken cancellationToken);

    /// <summary>
    /// For CheckNumber:ReuseScope = Tenant: the latest Yes/No answer each number received in a
    /// check that THIS organisation ran.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, SavedAnswer>> GetTenantAnswersAsync(
        Guid tenantId, IReadOnlyCollection<Guid> phoneNumberIds, CancellationToken cancellationToken);

    /// <summary>
    /// Stores fresh answers. Yes / No replace the stored status (and its date, result and
    /// organisation); Unknown leaves an earlier definitive answer untouched. When
    /// <paramref name="countAsNewCheck"/> is true every number's check_count goes up by one
    /// (false when a result file is processed again after a restart, so nothing is counted twice).
    /// Runs on the caller's connection and transaction.
    /// </summary>
    Task ApplyAnswersAsync(
        IReadOnlyCollection<RegistryAnswer> answers,
        Guid tenantId,
        DateTimeOffset checkedAtUtc,
        bool countAsNewCheck,
        CancellationToken cancellationToken);
}
