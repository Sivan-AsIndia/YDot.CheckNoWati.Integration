using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Pipeline;

/// <summary>What the job processor should do after a stage ran.</summary>
public enum StageOutcome
{
    /// <summary>The job moved to its next status: run the next stage straight away.</summary>
    Continue,

    /// <summary>The job is parked until its NextAttemptAtUtc (task still processing, WATI job still sending).</summary>
    Wait,

    /// <summary>The job reached a final status, or there is nothing this instance may do with it.</summary>
    Stop,
}

/// <summary>
/// One stage of the pipeline. Each stage handles one or more job statuses, does its work, saves,
/// and moves the job on.
///
/// EVERY STAGE MUST BE SAFE TO RUN TWICE. The worker can stop at any moment (deploy, crash,
/// power cut), and the next run starts the stage again from the job's stored status. A stage
/// therefore checks what is already done (a task id stored, an archive on disk, messages already
/// accepted) before doing anything with a side effect outside the database.
/// </summary>
public interface IJobStage
{
    /// <summary>The job statuses this stage runs for.</summary>
    IReadOnlyCollection<IntegrationJobStatus> Handles { get; }

    Task<StageOutcome> ExecuteAsync(IntegrationJob job, CancellationToken cancellationToken);
}
