using YDot.Integration.Application.Wati;

namespace YDot.Integration.Application.Inbox;

/// <summary>
/// The JSON file the CAM API writes into the inbox beside every web upload, named
/// {upload-reference}.manifest.json. It says who uploaded the file, for which organisation and
/// campaign, and with which options.
///
/// THE CAM API WRITES THE MANIFEST FIRST AND THE DATA FILE SECOND (each to a temporary name and
/// renamed into place), so by the time the worker sees {upload-reference}.csv its manifest is
/// already there. THIS IS A CONTRACT WITH THE CAM API - add properties, never rename them.
/// </summary>
public sealed record UploadManifest
{
    public int SchemaVersion { get; init; } = 1;

    /// <summary>e.g. UPL-20261010-7K2M9Q - also the inbox file name.</summary>
    public string? UploadReference { get; init; }

    public Guid TenantId { get; init; }

    public string? TenantCode { get; init; }

    public string? TenantName { get; init; }

    public Guid? CampaignId { get; init; }

    public string? CampaignCode { get; init; }

    public string? CampaignName { get; init; }

    public Guid? UploadedByUserId { get; init; }

    public string? UploadedByName { get; init; }

    /// <summary>The file name on the person's computer.</summary>
    public string? OriginalFileName { get; init; }

    public long? FileSizeBytes { get; init; }

    /// <summary>SHA-256 the API computed, checked against the file the worker reads.</summary>
    public string? Sha256 { get; init; }

    public DateTimeOffset? UploadedAtUtc { get; init; }

    public UploadOptions? Options { get; init; }

    public UploadWatiSettings? Wati { get; init; }
}

/// <summary>Per-upload switches. They can only narrow the worker's own Pipeline settings.</summary>
public sealed record UploadOptions
{
    /// <summary>"Check them again anyway": ignore saved answers and pay to check every number.</summary>
    public bool ForceRecheck { get; init; }

    /// <summary>False = do not send fresh numbers to CheckNumber.ai for this upload.</summary>
    public bool? RunCheckNumber { get; init; }

    /// <summary>False = do not message the verified numbers automatically for this upload.</summary>
    public bool? RunWati { get; init; }

    /// <summary>Process the file even if the same content was already processed for the same campaign.</summary>
    public bool AllowDuplicateFile { get; init; }
}

/// <summary>Job-specific WATI settings; anything left empty falls back to the worker's defaults.</summary>
public sealed record UploadWatiSettings
{
    public string? TemplateName { get; init; }

    public string? BroadcastName { get; init; }

    public List<TemplateParameter>? Parameters { get; init; }
}
