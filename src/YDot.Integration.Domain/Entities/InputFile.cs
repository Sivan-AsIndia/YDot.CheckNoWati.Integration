using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// One mobile-number file the worker found in the inbox (table <c>int_input_files</c>).
///
/// THIS IS THE START OF THE AUDIT CHAIN: input file -> integration job -> CheckNumber.ai task
/// -> results -> WATI job -> messages. Every file that reaches the inbox gets a row, including
/// the ones that are refused (duplicate content, unsupported type, no organisation), so the
/// database can answer "what happened to the file I uploaded?" in every case.
///
/// DUPLICATE PROTECTION is the <see cref="DedupeKey"/>: organisation + SHA-256 of the content +
/// campaign. A unique index on it means the same file uploaded twice for the same campaign can
/// never create a second job (and therefore never a second paid CheckNumber.ai task), even if
/// two worker instances see it at the same moment. A deliberate re-run ("check them again
/// anyway") adds the upload reference to the key so it is allowed.
/// </summary>
public sealed class InputFile : Entity, ITenantScoped
{
    /// <summary>Organisation that owns the file (iam_tenants.id).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Organisation subdomain/code at upload time, for logs and support.</summary>
    public string? TenantCode { get; set; }

    public string? TenantName { get; set; }

    /// <summary>
    /// The reference the CAM API gave the upload (e.g. UPL-20261010-7K2M9Q). It is the name of the
    /// file and its manifest in the inbox, and what the upload page polls until the job exists.
    /// Null for files copied into the inbox by hand.
    /// </summary>
    public string? UploadReference { get; set; }

    public InputFileSource Source { get; set; }

    public InputFileStatus Status { get; set; }

    /// <summary>The file name as the person uploaded it.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    public FileKind FileKind { get; set; }

    public long FileSizeBytes { get; set; }

    /// <summary>Lower-case hex SHA-256 of the file content.</summary>
    public string ContentSha256 { get; set; } = string.Empty;

    /// <summary>
    /// "tenant:sha256:campaign" (plus ":upload-reference" for a deliberate re-run). Unique; null for
    /// rows that were refused, so a refused file never blocks a later good one.
    /// </summary>
    public string? DedupeKey { get; set; }

    /// <summary>For a <see cref="InputFileStatus.Duplicate"/> row: the file that was processed first.</summary>
    public Guid? DuplicateOfInputFileId { get; set; }

    public InputFile? DuplicateOf { get; set; }

    /// <summary>Where the original file is kept now, relative to the worker data root.</summary>
    public string StoredFilePath { get; set; } = string.Empty;

    /// <summary>The upload manifest exactly as the CAM API wrote it (jsonb), for audit.</summary>
    public string? ManifestJson { get; set; }

    /// <summary>The campaign the outreach belongs to (cam_campaigns.id), when one was chosen.</summary>
    public Guid? CampaignId { get; set; }

    public string? CampaignCode { get; set; }

    public string? CampaignName { get; set; }

    /// <summary>The signed-in user who uploaded it (iam_users.id); null for folder drops.</summary>
    public Guid? UploadedByUserId { get; set; }

    public string? UploadedByName { get; set; }

    /// <summary>Number of sheets read (1 for .txt and .csv).</summary>
    public int SheetCount { get; set; }

    /// <summary>Why the file was refused, for <see cref="InputFileStatus.Rejected"/> and duplicates.</summary>
    public string? RejectionReason { get; set; }

    /// <summary>When the file was first seen in the inbox.</summary>
    public DateTimeOffset ReceivedAtUtc { get; set; }

    /// <summary>When the row (and its job) was committed.</summary>
    public DateTimeOffset? RegisteredAtUtc { get; set; }

    public IntegrationJob? Job { get; set; }
}
