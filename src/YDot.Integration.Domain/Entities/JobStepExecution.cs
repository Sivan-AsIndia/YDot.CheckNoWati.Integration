using YDot.Integration.Domain.Common;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// One execution of one pipeline step for one job (table <c>int_job_steps</c>) - the job's
/// timeline. A step that runs again after a restart gets a new row with the next
/// <see cref="Attempt"/>, so the history shows exactly what was redone and when.
/// </summary>
public sealed class JobStepExecution : Entity
{
    public Guid JobId { get; set; }

    /// <summary>See <c>StepCodes</c>.</summary>
    public string StepCode { get; set; } = string.Empty;

    public StepStatus Status { get; set; }

    /// <summary>1 for the first execution of this step in this job, 2 for the next ...</summary>
    public int Attempt { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public long? DurationMs { get; set; }

    /// <summary>What the step did, in one sentence.</summary>
    public string? Message { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Step-specific figures (jsonb), e.g. counts or the task id.</summary>
    public string? DetailsJson { get; set; }

    /// <summary>The worker instance that ran it.</summary>
    public string? WorkerInstance { get; set; }
}
