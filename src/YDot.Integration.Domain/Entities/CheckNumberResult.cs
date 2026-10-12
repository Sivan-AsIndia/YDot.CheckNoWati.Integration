using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// CheckNumber.ai's answer for one submitted number (table <c>int_checknumber_results</c>).
///
/// WHICH FIELD SAYS "HAS WHATSAPP": the result archive's all.csv has two columns, number and
/// activated; activated = yes means the number is registered on WhatsApp. When all.csv is
/// missing, activated.txt (yes) and unregistered.txt (no) carry the same information.
///
/// CheckNumber.ai returns digits without "+", and has been seen to put the task's main country
/// code in front of foreign numbers (+14155529876 came back as 9114155529876). Such a row
/// describes a different number, so it is stored as <see cref="ResultValidationStatus.Rewritten"/>
/// with WhatsApp <see cref="WhatsAppStatus.Unknown"/> rather than trusted.
/// </summary>
public sealed class CheckNumberResult : Entity, ITenantScoped
{
    public Guid TenantId { get; set; }

    public Guid CheckNumberTaskId { get; set; }

    public Guid JobId { get; set; }

    public Guid PhoneNumberId { get; set; }

    public PhoneNumber? PhoneNumber { get; set; }

    /// <summary>The number exactly as uploaded (E.164 with "+").</summary>
    public string SubmittedNumber { get; set; } = string.Empty;

    /// <summary>The number CheckNumber.ai reported on (digits), when it reported at all.</summary>
    public string? ReturnedNumber { get; set; }

    /// <summary>The raw "activated" value (yes / no / anything else).</summary>
    public string? ActivatedRaw { get; set; }

    public WhatsAppStatus WhatsAppStatus { get; set; }

    public ResultValidationStatus ValidationStatus { get; set; }

    /// <summary>Plain-language note, e.g. why the answer was not used (FailureReason).</summary>
    public string? Note { get; set; }

    public DateTimeOffset CheckedAtUtc { get; set; }
}
