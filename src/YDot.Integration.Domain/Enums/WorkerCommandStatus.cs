namespace YDot.Integration.Domain.Enums;

/// <summary>Progress of a worker command.</summary>
public enum WorkerCommandStatus
{
    /// <summary>Waiting for the worker.</summary>
    Pending,

    /// <summary>Claimed by a worker instance.</summary>
    Processing,

    /// <summary>Carried out; ResultMessage and ResultEntityId describe the outcome.</summary>
    Completed,

    /// <summary>Could not be carried out because of an error.</summary>
    Failed,

    /// <summary>Refused by a business rule (for example the job is not in a state that can be retried).</summary>
    Rejected
}
