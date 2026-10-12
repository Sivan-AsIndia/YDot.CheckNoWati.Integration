using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// One meaningful event (table <c>int_job_events</c>): a file registered, numbers reused, a
/// task created, a retry scheduled, a batch sent. Append-only.
///
/// An event can belong to an input file (a rejected file has no job), to an integration job, to
/// a WATI job (a manual broadcast has no integration job), or to several of them.
/// </summary>
public sealed class JobEvent : Entity
{
    public Guid? TenantId { get; set; }

    public Guid? InputFileId { get; set; }

    public Guid? JobId { get; set; }

    public Guid? WatiJobId { get; set; }

    public string? StepCode { get; set; }

    public EventLevel Level { get; set; }

    /// <summary>See <c>EventCodes</c>.</summary>
    public string EventCode { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>Structured details (jsonb).</summary>
    public string? DataJson { get; set; }

    public string? WorkerInstance { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}
