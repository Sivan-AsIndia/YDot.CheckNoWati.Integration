using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Domain.Entities;

/// <summary>
/// The rules about integration job statuses, in one place so the worker, the command
/// processor and the documentation agree.
/// </summary>
public static class JobStatusRules
{
    private static readonly HashSet<IntegrationJobStatus> Terminal =
    [
        IntegrationJobStatus.Completed,
        IntegrationJobStatus.CompletedWithErrors,
        IntegrationJobStatus.Failed,
        IntegrationJobStatus.Cancelled,
    ];

    /// <summary>The job will not be picked up again unless somebody retries it.</summary>
    public static bool IsTerminal(IntegrationJobStatus status) => Terminal.Contains(status);

    /// <summary>A worker should pick the job up (when its NextAttemptAtUtc has passed).</summary>
    public static bool IsActive(IntegrationJobStatus status) => !Terminal.Contains(status);

    /// <summary>
    /// A failed or cancelled job may be retried; it resumes from the stage it stopped in.
    ///
    /// So may a job that completed WITH ERRORS - the check finished but WATI turned the messages
    /// down (an expired token, an outage). Retrying it repeats nothing that succeeded: no new
    /// CheckNumber.ai task is created and nothing is paid for again; only the messages that failed
    /// for a reason a retry can fix go back to the queue.
    ///
    /// A cleanly completed job is never run again - re-upload the file with "check again" instead.
    /// </summary>
    public static bool CanRetry(IntegrationJobStatus status) =>
        status is IntegrationJobStatus.Failed or IntegrationJobStatus.Cancelled or IntegrationJobStatus.CompletedWithErrors;

    /// <summary>Only a job that has not finished can be cancelled.</summary>
    public static bool CanCancel(IntegrationJobStatus status) => !Terminal.Contains(status);
}
