using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// One HTTP call to CheckNumber.ai, its result storage, or WATI (table <c>int_api_call_logs</c>),
/// one row per ATTEMPT - so a retried call shows each attempt with its own status and timing.
///
/// SECRETS ARE NEVER STORED. The API key and bearer token travel in headers, which are not
/// recorded; the S3 signature is removed from result URLs; request and response bodies are
/// truncated.
/// </summary>
public sealed class ApiCallLog : Entity
{
    public ApiProvider Provider { get; set; }

    /// <summary>CreateTask, GetTask, DownloadResult, SendTemplateMessages, GetMessage, GetTemplates.</summary>
    public string Operation { get; set; } = string.Empty;

    public Guid? TenantId { get; set; }

    public Guid? JobId { get; set; }

    public Guid? WatiJobId { get; set; }

    /// <summary>The task, batch or message row the call was about.</summary>
    public Guid? RelatedEntityId { get; set; }

    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>The URL without query-string secrets.</summary>
    public string Url { get; set; } = string.Empty;

    public string? RequestBody { get; set; }

    public int? ResponseStatusCode { get; set; }

    public string? ResponseBody { get; set; }

    public long DurationMs { get; set; }

    /// <summary>1 for the first attempt of a call, 2 for the first retry ...</summary>
    public int Attempt { get; set; }

    public bool IsSuccess { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }
}
