namespace YDot.Integration.Domain.Enums;

/// <summary>Severity of a row in int_job_events.</summary>
public enum EventLevel
{
    /// <summary>Normal progress.</summary>
    Information,

    /// <summary>Something worth attention that did not stop the job (a retry, a rewritten number).</summary>
    Warning,

    /// <summary>Something that stopped a step or the job.</summary>
    Error
}
