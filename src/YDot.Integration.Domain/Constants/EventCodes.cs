namespace YDot.Integration.Domain.Constants;

/// <summary>
/// The event_code values of int_job_events: a narrative of what happened to a file or job,
/// one row per meaningful event, readable without opening a log file.
/// </summary>
public static class EventCodes
{
    public const string FileDetected = "FILE_DETECTED";
    public const string FileRegistered = "FILE_REGISTERED";
    public const string FileDuplicate = "FILE_DUPLICATE";
    public const string FileRejected = "FILE_REJECTED";
    public const string JobCreated = "JOB_CREATED";
    public const string ParseCompleted = "PARSE_COMPLETED";
    public const string NumbersReused = "NUMBERS_REUSED";
    public const string CheckNumberSkipped = "CHECKNUMBER_SKIPPED";
    public const string TaskCreated = "TASK_CREATED";
    public const string TaskPolled = "TASK_POLLED";
    public const string TaskExported = "TASK_EXPORTED";
    public const string ResultDownloaded = "RESULT_DOWNLOADED";
    public const string ResultExtracted = "RESULT_EXTRACTED";
    public const string ResultsProcessed = "RESULTS_PROCESSED";
    public const string NumbersRewritten = "NUMBERS_REWRITTEN";
    public const string VerifiedNumbersIdentified = "VERIFIED_NUMBERS_IDENTIFIED";
    public const string WatiJobCreated = "WATI_JOB_CREATED";
    public const string WatiSkipped = "WATI_SKIPPED";
    public const string WatiBatchSent = "WATI_BATCH_SENT";
    public const string WatiBatchFailed = "WATI_BATCH_FAILED";
    public const string WatiBatchUncertain = "WATI_BATCH_UNCERTAIN";
    public const string WatiJobCompleted = "WATI_JOB_COMPLETED";
    public const string RetryScheduled = "RETRY_SCHEDULED";
    public const string OutputWritten = "OUTPUT_WRITTEN";
    public const string JobCompleted = "JOB_COMPLETED";
    public const string JobFailed = "JOB_FAILED";
    public const string JobCancelled = "JOB_CANCELLED";
    public const string JobRetried = "JOB_RETRIED";
    public const string JobResumed = "JOB_RESUMED";
}
