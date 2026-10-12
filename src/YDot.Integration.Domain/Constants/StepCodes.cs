namespace YDot.Integration.Domain.Constants;

/// <summary>
/// The step names written to int_job_steps, int_job_events and every log line, so a job's
/// history reads the same in the database, in the log file and in the retry messages
/// ("... occurred during job INT-20261010-000001 at step WATI_SEND_BATCH").
/// </summary>
public static class StepCodes
{
    public const string RegisterFile = "REGISTER_FILE";
    public const string ParseFile = "PARSE_FILE";
    public const string CheckNumberCreateTask = "CHECKNUMBER_CREATE_TASK";
    public const string CheckNumberPoll = "CHECKNUMBER_POLL";
    public const string CheckNumberDownload = "CHECKNUMBER_DOWNLOAD";
    public const string CheckNumberExtract = "CHECKNUMBER_EXTRACT";
    public const string CheckNumberProcessResults = "CHECKNUMBER_PROCESS_RESULTS";
    public const string IdentifyVerified = "IDENTIFY_VERIFIED";
    public const string WatiPrepare = "WATI_PREPARE";
    public const string WatiSendBatch = "WATI_SEND_BATCH";
    public const string WatiAwait = "WATI_AWAIT";
    public const string WatiStatusSync = "WATI_STATUS_SYNC";
    public const string GenerateOutput = "GENERATE_OUTPUT";
    public const string CompleteJob = "COMPLETE_JOB";
}
