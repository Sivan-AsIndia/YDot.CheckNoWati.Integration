namespace YDot.Integration.Domain.Enums;

/// <summary>The lifecycle of one integration job. The order of the members is the order of the pipeline.</summary>
public enum IntegrationJobStatus
{
    /// <summary>Registered and waiting for a worker to pick it up.</summary>
    Queued,

    /// <summary>Reading the file, normalising numbers, finding duplicates and numbers already checked.</summary>
    Parsing,

    /// <summary>CheckNumber.ai tasks are being created, polled, downloaded and read.</summary>
    CheckingNumbers,

    /// <summary>Working out which numbers are on WhatsApp (fresh checks plus saved answers).</summary>
    Verifying,

    /// <summary>A WATI job is sending the template message to the verified numbers.</summary>
    SendingWati,

    /// <summary>Writing the completed output files.</summary>
    GeneratingOutput,

    /// <summary>Finished; every step succeeded.</summary>
    Completed,

    /// <summary>Finished, but some WATI messages failed.</summary>
    CompletedWithErrors,

    /// <summary>Stopped by an error. The error code and message say why; a retry resumes where it stopped.</summary>
    Failed,

    /// <summary>Stopped on request.</summary>
    Cancelled
}
