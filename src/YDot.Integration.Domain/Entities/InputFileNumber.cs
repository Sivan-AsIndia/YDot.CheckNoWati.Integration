using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// One number (or one unusable line) found in an input file (table <c>int_input_file_numbers</c>).
///
/// THE ORIGINAL TEXT IS NEVER MODIFIED. <see cref="RawValue"/> is exactly what the file said
/// ("Ravi 98765-43210", "098765 43210"); <see cref="NormalizedNumber"/> is what the worker made
/// of it (+919876543210). Both are kept so any decision can be explained later.
///
/// EVERY OCCURRENCE IS A ROW, duplicates included. A number that appears three times is checked
/// once (<see cref="NumberClassification.Fresh"/> or <see cref="NumberClassification.AlreadyChecked"/>
/// on its first row) and recorded twice more as <see cref="NumberClassification.Duplicate"/>,
/// each pointing at the first occurrence.
/// </summary>
public sealed class InputFileNumber : Entity, ITenantScoped
{
    public Guid TenantId { get; set; }

    public Guid InputFileId { get; set; }

    public Guid JobId { get; set; }

    /// <summary>
    /// 0-based position of this occurrence in the file (sheet by sheet, row by row), so reports and
    /// the completed output list numbers in the order the file has them.
    /// </summary>
    public int Sequence { get; set; }

    /// <summary>"Text file", "CSV file" or the Excel sheet name.</summary>
    public string SheetName { get; set; } = string.Empty;

    /// <summary>
    /// 1-based line in a .txt/.csv file; for Excel, the 1-based position among the sheet's
    /// non-blank rows (the same numbering the upload page shows).
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>The text exactly as it appeared in the file (OriginalNumber).</summary>
    public string RawValue { get; set; } = string.Empty;

    /// <summary>E.164 form (+919876543210); null for an invalid line.</summary>
    public string? NormalizedNumber { get; set; }

    public Guid? PhoneNumberId { get; set; }

    public PhoneNumber? PhoneNumber { get; set; }

    /// <summary>Calling code without "+", e.g. "91".</summary>
    public string? CountryCode { get; set; }

    /// <summary>ISO-style code shown on the screens, e.g. "IN" ("--" when unknown).</summary>
    public string? CountryIso { get; set; }

    public string? CountryName { get; set; }

    public NumberClassification Classification { get; set; }

    /// <summary>For a duplicate occurrence: the first occurrence of the same number in the file.</summary>
    public Guid? DuplicateOfId { get; set; }

    /// <summary>Why an invalid line was left out ("Too short (9 digits) and no country code").</summary>
    public string? InvalidReason { get; set; }

    /// <summary>For an already-checked number: the saved answer that was reused.</summary>
    public WhatsAppStatus? ReusedWhatsAppStatus { get; set; }

    public DateTimeOffset? ReusedCheckedAtUtc { get; set; }

    /// <summary>The int_checknumber_results row the saved answer came from.</summary>
    public Guid? ReusedFromResultId { get; set; }

    /// <summary>For a fresh number: the CheckNumber.ai task it was (or will be) submitted in.</summary>
    public Guid? CheckNumberTaskId { get; set; }

    /// <summary>
    /// The answer this job settled on for the number (fresh check or saved answer); set when the
    /// job identifies its WhatsApp-verified numbers, and copied onto duplicate occurrences.
    /// </summary>
    public WhatsAppStatus? FinalWhatsAppStatus { get; set; }

    /// <summary>The int_checknumber_results row behind <see cref="FinalWhatsAppStatus"/>.</summary>
    public Guid? CheckResultId { get; set; }

    /// <summary>Plain-language note shown in the report ("Answered from an earlier check - not charged").</summary>
    public string? ResultNote { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
