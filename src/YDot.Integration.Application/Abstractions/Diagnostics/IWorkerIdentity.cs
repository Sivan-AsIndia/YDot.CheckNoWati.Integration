namespace YDot.Integration.Application.Abstractions.Diagnostics;

/// <summary>Who "this worker" is: the name written into locked_by, step rows and log lines.</summary>
public interface IWorkerIdentity
{
    /// <summary>Primary key of this process's int_worker_instances row.</summary>
    Guid InstanceId { get; }

    /// <summary>Configured Worker:InstanceName, or "{machine}-{pid}".</summary>
    string InstanceName { get; }

    DateTimeOffset StartedAtUtc { get; }
}
