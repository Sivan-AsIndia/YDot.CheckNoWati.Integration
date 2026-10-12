using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// The downloaded CheckNumber.ai result archive and its extraction (table
/// <c>int_checknumber_result_files</c>), one per task.
///
/// Records everything the download and the unpacking did: when they started and finished, the
/// HTTP status, the size and SHA-256 of the archive, where it was saved
/// (data/checknumber/{job-number}/...), what was inside it, and any error - so a result can be
/// traced from the task_id all the way to the files on disk.
/// </summary>
public sealed class CheckNumberResultFile : Entity
{
    public Guid CheckNumberTaskId { get; set; }

    public Guid JobId { get; set; }

    /// <summary>The CheckNumber.ai task_id, repeated for convenience in support queries.</summary>
    public string ProviderTaskId { get; set; } = string.Empty;

    /// <summary>The link the archive was downloaded from (pre-signed; never logged).</summary>
    public string? ResultUrl { get; set; }

    // ---- Download --------------------------------------------------------------------------

    public TransferStatus DownloadStatus { get; set; } = TransferStatus.Pending;

    public int DownloadAttempts { get; set; }

    public int? HttpStatusCode { get; set; }

    public long? FileSizeBytes { get; set; }

    public string? FileSha256 { get; set; }

    /// <summary>The archive on disk, relative to the data root (data/checknumber/{job}/result.zip).</summary>
    public string? LocalZipPath { get; set; }

    public DateTimeOffset? DownloadStartedAtUtc { get; set; }

    public DateTimeOffset? DownloadCompletedAtUtc { get; set; }

    public long? DownloadDurationMs { get; set; }

    public string? DownloadError { get; set; }

    // ---- Extraction ---------------------------------------------------------------------------

    public TransferStatus ExtractionStatus { get; set; } = TransferStatus.Pending;

    /// <summary>The folder the archive was unpacked into, relative to the data root.</summary>
    public string? ExtractedPath { get; set; }

    public int? ExtractedFileCount { get; set; }

    /// <summary>JSON array of {name, size} for every entry of the archive.</summary>
    public string? ExtractedEntriesJson { get; set; }

    public DateTimeOffset? ExtractedAtUtc { get; set; }

    public string? ExtractionError { get; set; }

    // ---- Reading the results --------------------------------------------------------------------

    /// <summary>"all.csv", or "activated.txt+unregistered.txt" when all.csv was absent.</summary>
    public string? ResultSource { get; set; }

    public int? ParsedRowCount { get; set; }

    public int? MatchedCount { get; set; }

    public int? RewrittenCount { get; set; }

    public int? MissingCount { get; set; }

    /// <summary>Result rows that matched no submitted number.</summary>
    public int? UnmatchedRowCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
