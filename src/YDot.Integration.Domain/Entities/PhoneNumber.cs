using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// The number registry (table <c>int_phone_numbers</c>): one row per distinct E.164 number the
/// platform has ever seen, holding the latest DEFINITIVE WhatsApp answer CheckNumber.ai gave.
///
/// THIS IS THE COST SAVING. Before a file's numbers are sent to CheckNumber.ai, each one is
/// looked up here; a number that already has a Yes or No (inside the configured validity
/// window) is answered from this row and never paid for again. Only Yes and No overwrite the
/// stored status - an "unknown" result never wipes out an earlier definitive answer.
///
/// IT IS GLOBAL, NOT PER ORGANISATION, because whether a phone number has WhatsApp is a fact
/// about the number, not about who uploaded it. Which organisation uploaded which number is
/// recorded in int_input_file_numbers. CheckNumber:ReuseScope = Tenant restricts reuse to
/// answers the same organisation paid for.
/// </summary>
public sealed class PhoneNumber : Entity
{
    /// <summary>"+919500873627". Unique.</summary>
    public string E164 { get; set; } = string.Empty;

    /// <summary>"919500873627" - the form CheckNumber.ai returns and WATI expects.</summary>
    public string Digits { get; set; } = string.Empty;

    /// <summary>Calling code without "+", e.g. "91".</summary>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>libphonenumber region (e.g. "IN"), when it can tell.</summary>
    public string? RegionCode { get; set; }

    public string? CountryIso { get; set; }

    public string? CountryName { get; set; }

    public WhatsAppStatus WhatsAppStatus { get; set; } = WhatsAppStatus.Unknown;

    /// <summary>When the stored answer was given by CheckNumber.ai.</summary>
    public DateTimeOffset? WhatsAppCheckedAtUtc { get; set; }

    /// <summary>The result row the stored answer came from.</summary>
    public Guid? LastCheckResultId { get; set; }

    /// <summary>The organisation whose check produced the stored answer.</summary>
    public Guid? LastCheckedTenantId { get; set; }

    /// <summary>How many times CheckNumber.ai has been asked about this number.</summary>
    public int CheckCount { get; set; }

    public DateTimeOffset FirstSeenAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
