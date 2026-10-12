namespace YDot.Integration.Domain.Enums;

/// <summary>The outcome of one step execution in int_job_steps.</summary>
public enum StepStatus
{
    /// <summary>Started and not finished (or the worker stopped while it ran).</summary>
    Running,

    /// <summary>Finished successfully.</summary>
    Succeeded,

    /// <summary>Finished for now and parked until later (for example: CheckNumber.ai task still processing).</summary>
    Waiting,

    /// <summary>Not needed for this job (for example: WATI disabled).</summary>
    Skipped,

    /// <summary>Finished with an error.</summary>
    Failed
}
