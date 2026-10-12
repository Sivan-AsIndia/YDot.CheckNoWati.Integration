using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YDot.Integration.Application.Abstractions.Diagnostics;
using YDot.Integration.Application.Abstractions.Persistence;
using YDot.Integration.Application.Common;
using YDot.Integration.Application.Options;
using YDot.Integration.Application.Wati;
using YDot.Integration.Domain.Constants;
using YDot.Integration.Domain.Entities;
using YDot.Integration.Domain.Enums;

namespace YDot.Integration.Application.Pipeline;

/// <summary>
/// Runs the ONE stage that matches a job's current status, inside one DI scope (one DbContext),
/// and turns whatever goes wrong into the right job state:
///
///   JobStageException (permanent) -> job Failed with its error code and message
///   transient failure             -> job keeps its status, NextAttemptAtUtc pushed back with a
///                                    growing delay; after Worker:MaxConsecutiveStageFailures in a
///                                    row the job is Failed
///   anything unexpected           -> job Failed with UNEXPECTED_ERROR (and the full exception logged)
/// </summary>
public sealed class JobStageRunner(
    IIntegrationDbContext db,
    IEnumerable<IJobStage> stages,
    IWorkerIdentity identity,
    IJobJournal journal,
    IOptions<WorkerOptions> workerOptions,
    TimeProvider time,
    ILogger<JobStageRunner> logger)
{
    public async Task<StageOutcome> RunNextAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await db.IntegrationJobs
            .Include(j => j.InputFile)
            .SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null || job.IsTerminal)
        {
            return StageOutcome.Stop;
        }

        if (!string.Equals(job.LockedBy, identity.InstanceName, StringComparison.Ordinal))
        {
            logger.LogWarning("Job {JobNumber} is no longer leased to this instance ({LockedBy}); leaving it.", job.JobNumber, job.LockedBy);
            return StageOutcome.Stop;
        }

        if (job.CancelRequested)
        {
            await CancelAsync(job, cancellationToken);
            return StageOutcome.Stop;
        }

        var stage = stages.FirstOrDefault(s => s.Handles.Contains(job.Status))
                    ?? throw new InvalidOperationException($"No pipeline stage handles job status {job.Status}.");

        try
        {
            return await stage.ExecuteAsync(job, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Stopping: job {JobNumber} stays at {Status} and resumes on the next start.", job.JobNumber, job.Status);
            throw;
        }
        catch (JobStageException exception) when (!exception.IsTransient)
        {
            await FailAsync(jobId, exception.ErrorCode, exception.Message, null, cancellationToken);
            return StageOutcome.Stop;
        }
        catch (Exception exception) when (TransientErrors.IsTransient(exception))
        {
            await DeferAsync(jobId, exception, cancellationToken);
            return StageOutcome.Stop;
        }
        catch (Exception exception)
        {
            await FailAsync(jobId, ErrorCodes.Unexpected, $"Unexpected error: {exception.Message}", exception, cancellationToken);
            return StageOutcome.Stop;
        }
    }

    /// <summary>Marks a job Failed. Reloads it first: the failed stage may have left half-made changes tracked.</summary>
    private async Task FailAsync(Guid jobId, string errorCode, string message, Exception? exception, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var job = await db.IntegrationJobs.SingleAsync(j => j.Id == jobId, CancellationToken.None);
        var now = time.GetUtcNow();

        var failedAt = job.CurrentStep ?? job.Status.ToString();
        job.Status = IntegrationJobStatus.Failed;
        job.ErrorCode = errorCode;
        job.ErrorMessage = Text.Truncate(message, 4000);
        job.OutcomeMessage = Text.Truncate(message, 2000);
        job.CompletedAtUtc = now;
        job.NextAttemptAtUtc = null;
        job.UpdatedAtUtc = now;
        await db.SaveChangesAsync(CancellationToken.None);

        if (exception is null)
        {
            logger.LogError("Job {JobNumber} failed at {Step} ({ErrorCode}): {Message}", job.JobNumber, failedAt, errorCode, message);
        }
        else
        {
            logger.LogError(exception, "Job {JobNumber} failed unexpectedly at {Step}.", job.JobNumber, failedAt);
        }

        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.JobFailed,
            Level = EventLevel.Error,
            Message = $"Job {job.JobNumber} failed at {failedAt}: {message}",
            TenantId = job.TenantId,
            InputFileId = job.InputFileId,
            JobId = job.Id,
            StepCode = failedAt,
            Data = new { errorCode, exception = exception?.GetType().Name },
        }, CancellationToken.None);
    }

    /// <summary>Parks a job after a transient failure, with a delay that doubles each time (15 s, 30 s, 1 min ... 15 min).</summary>
    private async Task DeferAsync(Guid jobId, Exception exception, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var job = await db.IntegrationJobs.SingleAsync(j => j.Id == jobId, CancellationToken.None);
        var maxFailures = workerOptions.Value.MaxConsecutiveStageFailures;

        job.ConsecutiveFailures++;

        if (job.ConsecutiveFailures >= maxFailures)
        {
            await db.SaveChangesAsync(CancellationToken.None);
            var code = exception is JobStageException stage ? stage.ErrorCode : ErrorCodes.Unexpected;
            await FailAsync(jobId, code,
                $"Gave up after {job.ConsecutiveFailures} temporary failures in a row. Last error: {exception.Message}", null, cancellationToken);
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Min(15 * Math.Pow(2, job.ConsecutiveFailures - 1), 900));
        job.NextAttemptAtUtc = time.GetUtcNow().Add(delay);
        job.UpdatedAtUtc = time.GetUtcNow();
        await db.SaveChangesAsync(CancellationToken.None);

        logger.LogWarning(
            "Job {JobNumber}: temporary failure at {Step} ({Failures}/{Max}); trying again in {Delay}. {Reason}",
            job.JobNumber, job.CurrentStep, job.ConsecutiveFailures, maxFailures, delay, exception.Message);

        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.RetryScheduled,
            Level = EventLevel.Warning,
            Message =
                $"Job [{job.JobNumber}] Step [{job.CurrentStep}] Attempt [{job.ConsecutiveFailures}] Error [{exception.Message}] "
                + $"Retrying after [{(int)delay.TotalSeconds}] seconds.",
            TenantId = job.TenantId,
            JobId = job.Id,
            StepCode = job.CurrentStep,
        }, CancellationToken.None);
    }

    /// <summary>Stops a job on request. Numbers already checked are kept; WATI messages not yet sent are skipped.</summary>
    private async Task CancelAsync(IntegrationJob job, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        var watiJobIds = await db.WatiJobs
            .Where(w => w.IntegrationJobId == job.Id)
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        if (watiJobIds.Count > 0)
        {
            await WatiCancellation.CancelQueuedAsync(db, watiJobIds, now, cancellationToken);
        }

        job.Status = IntegrationJobStatus.Cancelled;
        job.ErrorCode = ErrorCodes.CancelledByUser;
        job.OutcomeMessage = "Stopped on request before it finished. Numbers checked so far were kept.";
        job.CompletedAtUtc = now;
        job.NextAttemptAtUtc = null;
        job.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Job {JobNumber} cancelled on request.", job.JobNumber);
        await journal.EventAsync(new JournalEvent
        {
            EventCode = EventCodes.JobCancelled,
            Level = EventLevel.Warning,
            Message = $"Job {job.JobNumber} was cancelled at {job.CurrentStep ?? job.Status.ToString()}.",
            TenantId = job.TenantId,
            JobId = job.Id,
            StepCode = job.CurrentStep,
        }, cancellationToken);
    }
}
