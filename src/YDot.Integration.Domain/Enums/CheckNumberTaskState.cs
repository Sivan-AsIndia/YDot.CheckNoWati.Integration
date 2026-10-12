namespace YDot.Integration.Domain.Enums;

/// <summary>Our own view of a CheckNumber.ai task (the provider's raw status is kept separately).</summary>
public enum CheckNumberTaskState
{
    /// <summary>The create request is about to be sent or is in flight. If the worker stops here the outcome is uncertain.</summary>
    Creating,

    /// <summary>CheckNumber.ai returned a task_id; the task is being polled.</summary>
    Created,

    /// <summary>CheckNumber.ai reports status 'exported' with a result_url.</summary>
    Exported,

    /// <summary>The result archive is on disk.</summary>
    Downloaded,

    /// <summary>The archive has been unpacked safely.</summary>
    Extracted,

    /// <summary>Every result row has been matched to a submitted number and stored.</summary>
    Processed,

    /// <summary>CheckNumber.ai refused or failed the task.</summary>
    Failed,

    /// <summary>The create request may or may not have reached CheckNumber.ai (timeout or 5xx after sending). Never retried automatically, to avoid paying twice.</summary>
    Uncertain,

    /// <summary>Not exported within the configured number of polls. A retry resumes polling the same task.</summary>
    TimedOut
}
