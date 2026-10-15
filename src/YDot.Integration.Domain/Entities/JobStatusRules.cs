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
    /// A failed or cancelled job may be retried; it resumes from the stage it stopped in, and
    /// repeats nothing that succeeded: a finished CheckNumber.ai task is not created again and
    /// nothing is paid for twice.
    ///
    /// A job that completed - cleanly, or "with errors" (only older jobs, whose automatic WATI send
    /// failed) - is never run again. A check no longer sends anything, so there is nothing of a
    /// completed check left to retry; messages that failed are retried on their own send, from the
    /// WhatsApp Messages page (WorkerCommandType.RetryWatiJob). To check the numbers again,
    /// re-upload the file with "check again".
    /// </summary>
    public static bool CanRetry(IntegrationJobStatus status) =>
        status is IntegrationJobStatus.Failed or IntegrationJobStatus.Cancelled;

    /// <summary>A finished WATI job whose failed messages may be put back in the queue.</summary>
    public static bool CanRetryWati(WatiJobStatus status) =>
        status is WatiJobStatus.Failed or WatiJobStatus.CompletedWithErrors;

    /// <summary>Only a job that has not finished can be cancelled.</summary>
    public static bool CanCancel(IntegrationJobStatus status) => !Terminal.Contains(status);
}
